namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using System.Data.Common;
using System.Text.Json;
using System.Threading.Channels;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Porta da API para a carga de atualização da base dos Correios: recusa o envio quando já há carga
/// em andamento, grava e confere o pacote recebido, obtém a trava consultiva da carga, aplica a
/// guarda de versão progressiva e a repetição por resumo SHA-256, registra e enfileira a execução
/// (a trava segue com ela até o fim da carga) e expõe a consulta de estado. O arquivo do pacote passa
/// a ter o identificador da execução e é apagado em toda recusa.
/// </summary>
internal sealed partial class RegistroDasImportacoes : IGeoImportacaoService
{
    private const string PostgresUniqueViolation = "23505";

    private static readonly JsonSerializerOptions RelatorioJsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly GeoDbContext _contexto;
    private readonly IGeoImportacaoFila _fila;
    private readonly TravaConsultivaDaCargaDne _trava;
    private readonly ArquivoDoPacoteDne _arquivos;
    private readonly TimeProvider _relogio;
    private readonly ILogger<RegistroDasImportacoes> _logger;
    private readonly long _limiteDoPacote;

    public RegistroDasImportacoes(
        GeoDbContext contexto,
        IGeoImportacaoFila fila,
        TravaConsultivaDaCargaDne trava,
        ArquivoDoPacoteDne arquivos,
        TimeProvider relogio,
        ILogger<RegistroDasImportacoes> logger,
        IOptions<EtlOpcoes> opcoes)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(fila);
        ArgumentNullException.ThrowIfNull(trava);
        ArgumentNullException.ThrowIfNull(arquivos);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(opcoes);

        _contexto = contexto;
        _fila = fila;
        _trava = trava;
        _arquivos = arquivos;
        _relogio = relogio;
        _logger = logger;
        _limiteDoPacote = opcoes.Value.TamanhoMaximoDoPacote;
    }

    public async Task<Result<EnvioDoPacoteRegistrado>> IniciarAsync(
        Stream corpo,
        string disparadoPor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(corpo);

        if (await HaCargaEmAndamentoAsync(cancellationToken).ConfigureAwait(false))
        {
            LogRecusadoSemLerOCorpo(_logger);
            return Result<EnvioDoPacoteRegistrado>.Failure(ErrosDoPacoteDne.EmAndamento());
        }

        Result<PacoteDneRecebido> recebido = await _arquivos
            .ReceberAsync(corpo, Guid.NewGuid(), _limiteDoPacote, cancellationToken)
            .ConfigureAwait(false);

        return recebido.IsFailure
            ? Result<EnvioDoPacoteRegistrado>.Failure(recebido.Error!)
            : await IniciarDoPacoteAsync(recebido.Value!, disparadoPor, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<EnvioDoPacoteRegistrado>> IniciarDoPacoteAsync(
        PacoteDneRecebido pacote,
        string disparadoPor,
        CancellationToken cancellationToken)
    {
        Result<EnvioDoPacoteRegistrado>? envio = null;
        try
        {
            envio = await IniciarSobTravaAsync(pacote, disparadoPor, cancellationToken).ConfigureAwait(false);
            return envio;
        }
        finally
        {
            ApagarSeNaoEnfileirado(pacote, envio);
        }
    }

    private static void ApagarSeNaoEnfileirado(PacoteDneRecebido pacote, Result<EnvioDoPacoteRegistrado>? envio)
    {
        bool enfileirado = envio is { IsSuccess: true } && envio.Value!.Desfecho == DesfechoDoEnvioDoPacote.CargaEnfileirada;
        if (!enfileirado)
        {
            ArquivoDoPacoteDne.ApagarSeExistir(pacote.Caminho);
        }
    }

    private async Task<Result<EnvioDoPacoteRegistrado>> IniciarSobTravaAsync(
        PacoteDneRecebido pacote,
        string disparadoPor,
        CancellationToken cancellationToken)
    {
        IAsyncDisposable? trava = await _trava.TentarAdquirirAsync(cancellationToken).ConfigureAwait(false);
        if (trava is null)
        {
            LogConflito(_logger, pacote.Versao);
            return Result<EnvioDoPacoteRegistrado>.Failure(ErrosDoPacoteDne.EmAndamento());
        }

        Result<GeoImportacaoExecucao> criacao = await CriarExecucaoOuSoltarAsync(pacote, disparadoPor, trava).ConfigureAwait(false);
        if (criacao.IsFailure)
        {
            return Result<EnvioDoPacoteRegistrado>.Failure(criacao.Error!);
        }

        return await RegistrarSobTravaAsync(criacao.Value!, pacote, trava, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<GeoImportacaoExecucao>> CriarExecucaoOuSoltarAsync(
        PacoteDneRecebido pacote,
        string disparadoPor,
        IAsyncDisposable trava)
    {
        try
        {
            Result<GeoImportacaoExecucao> criacao = IniciarExecucao(pacote, disparadoPor);
            if (criacao.IsFailure)
            {
                await trava.DisposeAsync().ConfigureAwait(false);
            }

            return criacao;
        }
        catch
        {
            await trava.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ImportacaoGeoDto?> ObterAsync(Guid id, CancellationToken cancellationToken)
    {
        GeoImportacaoExecucao? execucao = await _contexto.ImportacaoExecucoes
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return execucao is null ? null : MapearDto(execucao);
    }

    private Task<bool> HaCargaEmAndamentoAsync(CancellationToken cancellationToken) =>
        _contexto.ImportacaoExecucoes
            .AsNoTracking()
            .AnyAsync(e => e.Status == StatusImportacao.EmAndamento, cancellationToken);

    private Result<GeoImportacaoExecucao> IniciarExecucao(PacoteDneRecebido pacote, string disparadoPor) =>
        GeoImportacaoExecucao.Iniciar(
            new PacoteRecebido(pacote.Versao, pacote.Sha256, pacote.TamanhoEmBytes),
            disparadoPor,
            _relogio.GetUtcNow());

    private async Task<Result<EnvioDoPacoteRegistrado>> RegistrarSobTravaAsync(
        GeoImportacaoExecucao execucao,
        PacoteDneRecebido pacote,
        IAsyncDisposable trava,
        CancellationToken cancellationToken)
    {
        CargaReservada carga = CargaReservada.Reservar(execucao.Id, trava);
        try
        {
            return await VerificarERegistrarAsync(execucao, pacote, carga, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await SoltarSeNaoEntregueAsync(execucao.Id, carga).ConfigureAwait(false);
        }
    }

    private async ValueTask SoltarSeNaoEntregueAsync(Guid execucaoId, CargaReservada carga)
    {
        if (carga.EntregueAFila)
        {
            return;
        }

        try
        {
            _arquivos.ApagarSeExistir(execucaoId);
        }
        finally
        {
            await carga.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<Result<EnvioDoPacoteRegistrado>> VerificarERegistrarAsync(
        GeoImportacaoExecucao execucao,
        PacoteDneRecebido pacote,
        CargaReservada carga,
        CancellationToken cancellationToken)
    {
        Result guarda = await VerificarVersaoProgressivaAsync(execucao.VersaoDataset, cancellationToken).ConfigureAwait(false);
        if (guarda.IsFailure)
        {
            return Result<EnvioDoPacoteRegistrado>.Failure(guarda.Error!);
        }

        Guid? vigente = await _contexto.ObterPublicacaoDoMesmoPacoteAsync(pacote.Sha256, cancellationToken).ConfigureAwait(false);
        if (vigente is not null)
        {
            LogPacoteJaPublicado(_logger, vigente.Value, execucao.VersaoDataset);
            return Result<EnvioDoPacoteRegistrado>.Success(new(vigente.Value, DesfechoDoEnvioDoPacote.JaPublicado));
        }

        return await PersistirEEnfileirarAsync(execucao, pacote, carga).ConfigureAwait(false);
    }

    private async Task<Result> VerificarVersaoProgressivaAsync(string versao, CancellationToken cancellationToken)
    {
        string? ultimaAplicada = await _contexto.ObterUltimaVersaoAplicadaAsync(cancellationToken).ConfigureAwait(false);
        if (ultimaAplicada is not null && string.CompareOrdinal(versao, ultimaAplicada) < 0)
        {
            LogVersaoNaoProgressiva(_logger, versao, ultimaAplicada);
            return Result.Failure(new DomainError(
                GeoImportacaoErrorCodes.VersaoNaoProgressiva,
                $"A versão {versao} é anterior à última aplicada ({ultimaAplicada})."));
        }

        return Result.Success();
    }

    private async Task<Result<EnvioDoPacoteRegistrado>> PersistirEEnfileirarAsync(
        GeoImportacaoExecucao execucao,
        PacoteDneRecebido pacote,
        CargaReservada carga)
    {
        _arquivos.AtribuirExecucao(pacote.Caminho, execucao.Id);
        if (!await TentarPersistirAsync(execucao).ConfigureAwait(false))
        {
            return Result<EnvioDoPacoteRegistrado>.Failure(ErrosDoPacoteDne.EmAndamento());
        }

        return await TentarEnfileirarAsync(execucao, carga).ConfigureAwait(false);
    }

    // Depois de obtida a trava, o registro não é cancelável pelo pedido: ou a execução fica registrada
    // e enfileirada, ou nada fica. Cancelar no meio deixaria uma execução em andamento sem dono.
    private async Task<bool> TentarPersistirAsync(GeoImportacaoExecucao execucao)
    {
        _contexto.ImportacaoExecucoes.Add(execucao);
        try
        {
            await _contexto.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (EhViolacaoUnica(ex))
        {
            _contexto.ImportacaoExecucoes.Entry(execucao).State = EntityState.Detached;
            LogConflito(_logger, execucao.VersaoDataset);
            return false;
        }
    }

    private async Task<Result<EnvioDoPacoteRegistrado>> TentarEnfileirarAsync(GeoImportacaoExecucao execucao, CargaReservada carga)
    {
        try
        {
            await _fila.EnfileirarAsync(carga, CancellationToken.None).ConfigureAwait(false);
            carga.MarcarEntregueAFila();
        }
        catch (ChannelClosedException ex)
        {
            await MarcarFalhaNoEnfileiramentoAsync(execucao.Id).ConfigureAwait(false);
            LogNaoEnfileirada(_logger, execucao.Id, DiagnosticoDeExcecaoSanitizado.Descrever(ex));
            return Result<EnvioDoPacoteRegistrado>.Failure(new DomainError(
                GeoImportacaoErrorCodes.NaoEnfileirada,
                "Não foi possível enfileirar a importação (serviço em desligamento)."));
        }

        LogDisparada(_logger, execucao.Id, execucao.VersaoDataset);
        return Result<EnvioDoPacoteRegistrado>.Success(new(execucao.Id, DesfechoDoEnvioDoPacote.CargaEnfileirada));
    }

    private async Task MarcarFalhaNoEnfileiramentoAsync(Guid execucaoId)
    {
        _contexto.ChangeTracker.Clear();
        try
        {
            await PersistirFalhaNoEnfileiramentoAsync(execucaoId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            LogFalhaPersistir(_logger, execucaoId, DiagnosticoDeExcecaoSanitizado.Descrever(ex));
        }
    }

    private async Task PersistirFalhaNoEnfileiramentoAsync(Guid execucaoId)
    {
        GeoImportacaoExecucao? atual = await _contexto.ImportacaoExecucoes
            .FirstOrDefaultAsync(e => e.Id == execucaoId, CancellationToken.None)
            .ConfigureAwait(false);

        if (atual is not null && atual.Status == StatusImportacao.EmAndamento)
        {
            atual.Falhar(_relogio.GetUtcNow(), "Serviço em desligamento; carga não enfileirada.");
            await _contexto.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static bool EhViolacaoUnica(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresUniqueViolation;

    private static PacoteDaImportacaoDto? MapearPacote(GeoImportacaoExecucao execucao) =>
        execucao.PacoteSha256 is not null && execucao.PacoteTamanhoEmBytes is not null
            ? new PacoteDaImportacaoDto(execucao.PacoteSha256, execucao.PacoteTamanhoEmBytes.Value)
            : null;

    private static ImportacaoGeoDto MapearDto(GeoImportacaoExecucao execucao)
    {
        RelatorioImportacaoDto? relatorio = execucao.RelatorioJson is null
            ? null
            : JsonSerializer.Deserialize<RelatorioImportacaoDto>(execucao.RelatorioJson, RelatorioJsonOptions);

        return new ImportacaoGeoDto(
            execucao.Id,
            execucao.VersaoDataset,
            execucao.Status.ToString(),
            execucao.IniciadoEm,
            execucao.ConcluidoEm,
            execucao.DisparadoPor,
            execucao.Mensagem,
            MapearPacote(execucao),
            relatorio);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: envio recusado sem ler o corpo — já há uma carga em andamento.")]
    private static partial void LogRecusadoSemLerOCorpo(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Geo: carga {ExecucaoId} disparada (versão {Versao}).")]
    private static partial void LogDisparada(ILogger logger, Guid execucaoId, string versao);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: disparo recusado (versão {Versao}) — já há uma carga em andamento.")]
    private static partial void LogConflito(ILogger logger, string versao);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: execução {ExecucaoId} não enfileirada (serviço em desligamento). Diagnóstico: {Diagnostico}")]
    private static partial void LogNaoEnfileirada(ILogger logger, Guid execucaoId, string diagnostico);

    [LoggerMessage(Level = LogLevel.Information, Message = "Geo: pacote da versão {Versao} já é o da carga concluída {ExecucaoId}; nada a repetir.")]
    private static partial void LogPacoteJaPublicado(ILogger logger, Guid execucaoId, string versao);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: disparo recusado — versão {Versao} anterior à última aplicada {UltimaVersao}.")]
    private static partial void LogVersaoNaoProgressiva(ILogger logger, string versao, string ultimaVersao);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: falha ao persistir o estado de falha da execução {ExecucaoId}. Diagnóstico: {Diagnostico}")]
    private static partial void LogFalhaPersistir(ILogger logger, Guid execucaoId, string diagnostico);
}
