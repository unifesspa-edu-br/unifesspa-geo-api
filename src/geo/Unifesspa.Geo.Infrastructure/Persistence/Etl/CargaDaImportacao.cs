namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using System.Data.Common;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Executa a carga DNE em segundo plano, sob a trava consultiva obtida no pedido, em transação
/// única no <see cref="GeoDbContext"/>, garantindo publicação atômica ou reversão integral. A fonte
/// da execução (esquema de carga e arquivo do pacote) é preparada e descartada pelo importador da
/// versão; o arquivo do pacote é apagado ao fim em qualquer desfecho, antes de soltar a trava.
/// </summary>
internal sealed partial class CargaDaImportacao : IGeoImportacaoExecutor
{
    private static readonly JsonSerializerOptions RelatorioJsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly GeoDbContext _contexto;
    private readonly ImportadorDaVersaoDne _importadorVersao;
    private readonly FinalizacaoDaCargaDne _finalizacao;
    private readonly TimeProvider _relogio;
    private readonly TimeSpan _duracaoMaximaDaCarga;
    private readonly ILogger<CargaDaImportacao> _logger;

    public CargaDaImportacao(
        GeoDbContext contexto,
        ImportadorDaVersaoDne importadorVersao,
        FinalizacaoDaCargaDne finalizacao,
        TimeProvider relogio,
        IOptions<EtlOpcoes> opcoes,
        ILogger<CargaDaImportacao> logger)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(importadorVersao);
        ArgumentNullException.ThrowIfNull(finalizacao);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(logger);

        _contexto = contexto;
        _importadorVersao = importadorVersao;
        _finalizacao = finalizacao;
        _relogio = relogio;
        _duracaoMaximaDaCarga = opcoes.Value.DuracaoMaximaDaCarga;
        _logger = logger;
    }

    public async Task ExecutarAsync(CargaReservada carga, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(carga);

        await using (carga.ConfigureAwait(false))
        {
            await ExecutarSeEmAndamentoAsync(carga.ExecucaoId, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task MarcarInterrompidaNoDesligamentoAsync(Guid execucaoId, CancellationToken cancellationToken) =>
        _finalizacao.MarcarFalhaAsync(execucaoId, "Carga interrompida no desligamento do processo em segundo plano.", cancellationToken);

    public Task MarcarFalhaInesperadaAsync(Guid execucaoId, CancellationToken cancellationToken) =>
        _finalizacao.MarcarFalhaAsync(execucaoId, "Falha inesperada ao iniciar a carga.", cancellationToken);

    private async Task ExecutarSeEmAndamentoAsync(Guid execucaoId, CancellationToken cancellationToken)
    {
        GeoImportacaoExecucao? execucao = await _contexto.ImportacaoExecucoes
            .FirstOrDefaultAsync(e => e.Id == execucaoId, CancellationToken.None)
            .ConfigureAwait(false);

        if (execucao is null || execucao.Status != StatusImportacao.EmAndamento)
        {
            LogExecucaoIgnorada(_logger, execucaoId);
            await _finalizacao.DescartarPacoteAsync(execucaoId).ConfigureAwait(false);
            return;
        }

        await ExecutarComPrazoAsync(execucao, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecutarComPrazoAsync(GeoImportacaoExecucao execucao, CancellationToken cancelamentoExterno)
    {
        long inicio = _relogio.GetTimestamp();
        DateTimeOffset inicioCarga = _relogio.GetUtcNow();
        using PrazoDaCarga prazo = new(_duracaoMaximaDaCarga, _relogio, cancelamentoExterno);

        try
        {
            await ExecutarTratandoInterrupcaoAsync(execucao, inicio, inicioCarga, prazo).ConfigureAwait(false);
        }
        finally
        {
            await _finalizacao.DescartarPacoteAsync(execucao.Id).ConfigureAwait(false);
        }
    }

    private async Task ExecutarTratandoInterrupcaoAsync(
        GeoImportacaoExecucao execucao,
        long inicio,
        DateTimeOffset inicioCarga,
        PrazoDaCarga prazo)
    {
        try
        {
            await ExecutarTratandoFalhaAsync(execucao, inicio, inicioCarga, prazo.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TratarInterrupcaoAsync(execucao.Id, execucao.VersaoDataset, inicio, prazo.FoiExcedido).ConfigureAwait(false);
            if (!prazo.FoiExcedido)
            {
                throw;
            }
        }
    }

    private async Task ExecutarTratandoFalhaAsync(
        GeoImportacaoExecucao execucao,
        long inicio,
        DateTimeOffset inicioCarga,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecutarEmTransacaoUnicaAsync(execucao, inicio, inicioCarga, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            await TratarFalhaGeralAsync(execucao.Id, execucao.VersaoDataset, inicio, excecao).ConfigureAwait(false);
        }
    }

    private async Task ExecutarEmTransacaoUnicaAsync(
        GeoImportacaoExecucao execucao,
        long inicio,
        DateTimeOffset inicioCarga,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction transacao = await _contexto.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transacao.ConfigureAwait(false))
        {
            await ProcessarTransacaoAsync(transacao, execucao, inicio, inicioCarga, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessarTransacaoAsync(
        IDbContextTransaction transacao,
        GeoImportacaoExecucao execucao,
        long inicio,
        DateTimeOffset inicioCarga,
        CancellationToken cancellationToken)
    {
        ResultadoDaVersao resultado = await _importadorVersao
            .ImportarAsync(execucao.Id, execucao.VersaoDataset, inicioCarga, cancellationToken)
            .ConfigureAwait(false);

        if (resultado.MotivoRecusa != MotivoRecusaDaVersao.Nenhum)
        {
            await RecusarVersaoAsync(transacao, execucao, inicio, resultado.MotivoRecusa).ConfigureAwait(false);
            return;
        }

        await ConcluirEConfirmarAsync(transacao, execucao, inicio, resultado.Relatorio!, cancellationToken).ConfigureAwait(false);
    }

    private async Task ConcluirEConfirmarAsync(
        IDbContextTransaction transacao,
        GeoImportacaoExecucao execucao,
        long inicio,
        RelatorioImportacaoDto relatorio,
        CancellationToken cancellationToken)
    {
        string relatorioJson = JsonSerializer.Serialize(relatorio, RelatorioJsonOptions);
        Result conclusao = execucao.Concluir(_relogio.GetUtcNow(), relatorioJson, ResumoConclusao(relatorio));
        if (conclusao.IsFailure)
        {
            LogTransicaoInesperada(_logger, execucao.Id, conclusao.Error!.Code);
        }

        if (await GravarConclusaoAsync(transacao, execucao, inicio, cancellationToken).ConfigureAwait(false))
        {
            await ConfirmarTransacaoAsync(transacao, execucao.Id, execucao.VersaoDataset, inicio, relatorio).ConfigureAwait(false);
        }
    }

    // A conclusão só vale se a execução ainda está em andamento. Se outro encerramento já a marcou
    // como falha, a publicação inteira, que está na mesma transação, é desfeita e a falha é mantida.
    private async Task<bool> GravarConclusaoAsync(
        IDbContextTransaction transacao,
        GeoImportacaoExecucao execucao,
        long inicio,
        CancellationToken cancellationToken)
    {
        try
        {
            await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transacao.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            _finalizacao.RegistrarMetricaFalha(execucao.VersaoDataset, inicio);
            LogConclusaoRecusada(_logger, execucao.Id, execucao.VersaoDataset);
            return false;
        }
    }

    private async Task ConfirmarTransacaoAsync(
        IDbContextTransaction transacao,
        Guid execucaoId,
        string versao,
        long inicio,
        RelatorioImportacaoDto relatorio)
    {
        try
        {
            await transacao.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            await LiberarTransacaoPendenteAsync(transacao).ConfigureAwait(false);
            await _finalizacao.TratarCommitIncertoAsync(execucaoId, versao, inicio, relatorio, excecao).ConfigureAwait(false);
            return;
        }

        await _finalizacao.ConcluirComSeloAsync(execucaoId, versao, inicio, relatorio).ConfigureAwait(false);
    }

    // Se a confirmação falhou sem chegar ao servidor, a transação segue aberta e segura a linha da
    // execução: sem desfazê-la, a conferência em nova conexão ficaria esperando por esse bloqueio.
    private static async Task LiberarTransacaoPendenteAsync(IDbContextTransaction transacao)
    {
        try
        {
            await transacao.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception excecao) when (excecao is DbException or InvalidOperationException)
        {
            // Conexão perdida: o servidor já encerrou a transação.
        }
    }

    private async Task RecusarVersaoAsync(
        IDbContextTransaction transacao,
        GeoImportacaoExecucao execucao,
        long inicio,
        MotivoRecusaDaVersao motivo)
    {
        await transacao.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        string mensagem = motivo == MotivoRecusaDaVersao.VersaoVazia
            ? "Versão da base dos Correios vazia ou sem o Brasil e suas cidades — carga recusada."
            : "Tabelas-folha (logradouro) vazias — carga recusada.";

        await _finalizacao.MarcarFalhaAsync(execucao.Id, mensagem, CancellationToken.None).ConfigureAwait(false);
        _finalizacao.RegistrarMetricaFalha(execucao.VersaoDataset, inicio);
        RegistrarLogRecusa(execucao.Id, execucao.VersaoDataset, motivo);
    }

    private void RegistrarLogRecusa(Guid execucaoId, string versao, MotivoRecusaDaVersao motivo)
    {
        if (motivo == MotivoRecusaDaVersao.VersaoVazia)
        {
            LogVersaoVazia(_logger, execucaoId, versao);
        }
        else
        {
            LogFolhasVazias(_logger, execucaoId, versao);
        }
    }

    private async Task TratarInterrupcaoAsync(Guid execucaoId, string versao, long inicio, bool estouroPrazo)
    {
        string mensagem = estouroPrazo
            ? "Carga interrompida ao exceder a duração máxima configurada."
            : "Carga interrompida (cancelamento/desligamento).";
        await _finalizacao.MarcarFalhaAsync(execucaoId, mensagem, CancellationToken.None).ConfigureAwait(false);
        _finalizacao.RegistrarMetricaFalha(versao, inicio);
        if (estouroPrazo)
        {
            LogPrazoExcedido(_logger, execucaoId, versao);
        }
        else
        {
            LogCancelada(_logger, execucaoId, versao);
        }
    }

    private async Task TratarFalhaGeralAsync(Guid execucaoId, string versao, long inicio, Exception excecao)
    {
        await _finalizacao
            .MarcarFalhaAsync(execucaoId, MensagemDaFalha(excecao), CancellationToken.None)
            .ConfigureAwait(false);
        _finalizacao.RegistrarMetricaFalha(versao, inicio);
        LogFalhou(_logger, execucaoId, versao, DiagnosticoDeExcecaoSanitizado.Descrever(excecao));
    }

    // A recusa do pacote traz só a tabela (da lista fechada), o número da linha e um motivo fixo.
    private static string MensagemDaFalha(Exception excecao) =>
        excecao is ErroDeLeituraDoPacoteDneException recusa
            ? $"Pacote recusado: {recusa.Message}"
            : "Falha na carga da DNE.";

    private static string ResumoConclusao(RelatorioImportacaoDto relatorio) =>
        $"{relatorio.Inseridos} inseridos, {relatorio.Atualizados} atualizados, " +
        $"{relatorio.Orfaos} órfãos, {relatorio.Degradados} degradados.";

    [LoggerMessage(Level = LogLevel.Information, Message = "Geo: execução {ExecucaoId} ignorada (inexistente ou já finalizada).")]
    private static partial void LogExecucaoIgnorada(ILogger logger, Guid execucaoId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: carga {ExecucaoId} falhou (versão {Versao}). Diagnóstico: {Diagnostico}")]
    private static partial void LogFalhou(ILogger logger, Guid execucaoId, string versao, string diagnostico);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: carga {ExecucaoId} interrompida por cancelamento/desligamento (versão {Versao}).")]
    private static partial void LogCancelada(ILogger logger, Guid execucaoId, string versao);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: carga {ExecucaoId} interrompida ao exceder a duração máxima (versão {Versao}).")]
    private static partial void LogPrazoExcedido(ILogger logger, Guid execucaoId, string versao);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: carga {ExecucaoId} recusada — versão {Versao} da base dos Correios vazia ou sem o Brasil e suas cidades (nenhuma cidade aplicada).")]
    private static partial void LogVersaoVazia(ILogger logger, Guid execucaoId, string versao);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: carga {ExecucaoId} recusada — versão {Versao} da base dos Correios sem nenhum logradouro (nenhum logradouro aplicado).")]
    private static partial void LogFolhasVazias(ILogger logger, Guid execucaoId, string versao);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: transição inesperada na execução {ExecucaoId} ({Codigo}).")]
    private static partial void LogTransicaoInesperada(ILogger logger, Guid execucaoId, string codigo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: conclusão da carga {ExecucaoId} (versão {Versao}) recusada: a execução já foi encerrada como falha e a publicação foi desfeita.")]
    private static partial void LogConclusaoRecusada(ILogger logger, Guid execucaoId, string versao);
}
