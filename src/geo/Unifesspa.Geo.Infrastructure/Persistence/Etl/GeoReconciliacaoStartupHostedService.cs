namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// Na partida, antes de o host aceitar disparos: limpa o diretório de pacotes (mesmo com a trava
/// ocupada); sob a trava consultiva (<c>pg_try_advisory_lock</c>) marca como falha as execuções
/// <c>EmAndamento</c> órfãs, descarta os esquemas de carga que sobraram e sincroniza o selo do cache
/// de CEP com a última execução concluída. Toda execução <c>EmAndamento</c> viva tem a trava desde o
/// pedido; por isso, com a trava livre, todas as que existem estão abandonadas, sem critério de
/// tempo. Com a trava ocupada, não faz mais nada: quem a detém sela ao confirmar.
/// </summary>
internal sealed partial class GeoReconciliacaoStartupHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TravaConsultivaDaCargaDne _trava;
    private readonly IFonteDaExecucaoDne _fonteDaExecucao;
    private readonly ArquivoDoPacoteDne _arquivos;
    private readonly TimeProvider _relogio;
    private readonly ILogger<GeoReconciliacaoStartupHostedService> _logger;

    public GeoReconciliacaoStartupHostedService(
        IServiceScopeFactory scopeFactory,
        TravaConsultivaDaCargaDne trava,
        IFonteDaExecucaoDne fonteDaExecucao,
        ArquivoDoPacoteDne arquivos,
        TimeProvider relogio,
        ILogger<GeoReconciliacaoStartupHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(trava);
        ArgumentNullException.ThrowIfNull(fonteDaExecucao);
        ArgumentNullException.ThrowIfNull(arquivos);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _trava = trava;
        _fonteDaExecucao = fonteDaExecucao;
        _arquivos = arquivos;
        _relogio = relogio;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LimparDiretorioDePacotes();
        try
        {
            await ReconciliarComTravaAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            LogErroReconciliar(_logger, DiagnosticoDeExcecaoSanitizado.Descrever(excecao));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void LimparDiretorioDePacotes()
    {
        try
        {
            int removidos = _arquivos.LimparDiretorio();
            if (removidos > 0)
            {
                LogPacotesResiduaisRemovidos(_logger, removidos);
            }
        }
        catch (Exception excecao) when (excecao is IOException or UnauthorizedAccessException)
        {
            LogErroLimparDiretorio(_logger, DiagnosticoDeExcecaoSanitizado.Descrever(excecao));
        }
    }

    private async Task ReconciliarComTravaAsync(CancellationToken cancellationToken)
    {
        IAsyncDisposable? travaAdquirida = await _trava
            .TentarAdquirirAsync(cancellationToken)
            .ConfigureAwait(false);

        if (travaAdquirida is null)
        {
            LogTravaOcupadaNaPartida(_logger);
            return;
        }

        await using (travaAdquirida.ConfigureAwait(false))
        {
            await ReconciliarEmEscopoAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DescartarEsquemasOrfaosAsync(CancellationToken cancellationToken)
    {
        int descartados = await _fonteDaExecucao.DescartarSobrasAsync(cancellationToken).ConfigureAwait(false);
        if (descartados > 0)
        {
            LogEsquemasOrfaosDescartados(_logger, descartados);
        }
    }

    private async Task ReconciliarEmEscopoAsync(CancellationToken cancellationToken)
    {
        using IServiceScope escopo = _scopeFactory.CreateScope();
        GeoDbContext contexto = escopo.ServiceProvider.GetRequiredService<GeoDbContext>();
        await FalharExecucoesOrfasAsync(contexto, cancellationToken).ConfigureAwait(false);
        await DescartarEsquemasOrfaosAsync(cancellationToken).ConfigureAwait(false);
        await SincronizarSeloDoCacheAsync(escopo.ServiceProvider, contexto, cancellationToken).ConfigureAwait(false);
    }

    private async Task FalharExecucoesOrfasAsync(GeoDbContext contexto, CancellationToken cancellationToken)
    {
        List<GeoImportacaoExecucao> orfas = await contexto.ImportacaoExecucoes
            .Where(e => e.Status == StatusImportacao.EmAndamento)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (orfas.Count > 0)
        {
            await MarcarOrfasComoFalhasAsync(contexto, orfas, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task MarcarOrfasComoFalhasAsync(
        GeoDbContext contexto,
        List<GeoImportacaoExecucao> orfas,
        CancellationToken cancellationToken)
    {
        DateTimeOffset agora = _relogio.GetUtcNow();
        foreach (GeoImportacaoExecucao execucao in orfas)
        {
            execucao.Falhar(agora, "Carga interrompida antes de concluir (reconciliada na partida).");
        }

        await contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogOrfasReconciliadas(_logger, orfas.Count);
    }

    private async Task SincronizarSeloDoCacheAsync(
        IServiceProvider serviceProvider,
        GeoDbContext contexto,
        CancellationToken cancellationToken)
    {
        GeoImportacaoExecucao? ultima = await ObterUltimaConcluidaAsync(contexto, cancellationToken).ConfigureAwait(false);
        if (ultima is not null)
        {
            await TentarSelarCacheAsync(serviceProvider, ultima, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task<GeoImportacaoExecucao?> ObterUltimaConcluidaAsync(GeoDbContext contexto, CancellationToken cancellationToken) =>
        contexto.ImportacaoExecucoes
            .AsNoTracking()
            .Where(e => e.Status == StatusImportacao.Concluida)
            .OrderByDescending(e => e.ConcluidoEm)
            .ThenByDescending(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task TentarSelarCacheAsync(IServiceProvider serviceProvider, GeoImportacaoExecucao ultima, CancellationToken cancellationToken)
    {
        try
        {
            IGeoCepCacheInvalidador invalidador = serviceProvider.GetRequiredService<IGeoCepCacheInvalidador>();
            await invalidador
                .InvalidarAsync(SeloDoCacheDeCep.Identificar(ultima.VersaoDataset, ultima.Id), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            LogErroSincronizarSelo(_logger, ultima.VersaoDataset, DiagnosticoDeExcecaoSanitizado.Descrever(excecao));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: {Afetadas} execução(ões) em andamento reconciliada(s) como falha no startup.")]
    private static partial void LogOrfasReconciliadas(ILogger logger, int afetadas);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: {Removidos} item(ns) residual(is) do diretório de pacotes removido(s) na partida.")]
    private static partial void LogPacotesResiduaisRemovidos(ILogger logger, int removidos);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: falha ao limpar o diretório de pacotes na partida. Diagnóstico: {Diagnostico}")]
    private static partial void LogErroLimparDiretorio(ILogger logger, string diagnostico);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: {Descartados} esquema(s) de carga órfão(s) descartado(s) na partida.")]
    private static partial void LogEsquemasOrfaosDescartados(ILogger logger, int descartados);

    [LoggerMessage(Level = LogLevel.Information, Message = "Geo: trava consultiva ocupada na partida — nenhuma execução foi alterada e o selo do cache não foi sincronizado.")]
    private static partial void LogTravaOcupadaNaPartida(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: falha ao reconciliar execuções em andamento no startup. Diagnóstico: {Diagnostico}")]
    private static partial void LogErroReconciliar(ILogger logger, string diagnostico);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: falha ao sincronizar o selo do cache para a versão {Versao} na partida. Diagnóstico: {Diagnostico}")]
    private static partial void LogErroSincronizarSelo(ILogger logger, string versao, string diagnostico);
}
