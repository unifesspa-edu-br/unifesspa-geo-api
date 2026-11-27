namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// Na partida, antes de o host aceitar disparos: limpa o diretório de pacotes (mesmo com a trava
/// ocupada); sob a trava consultiva (<c>pg_try_advisory_lock</c>) encerra as cargas abandonadas
/// (<see cref="EncerramentoDasCargasAbandonadas"/>) e sincroniza o selo do cache de CEP com a última
/// execução concluída. Toda execução <c>EmAndamento</c> viva tem a trava desde o pedido; por isso,
/// com a trava livre, todas as que existem estão abandonadas, sem critério de tempo. Com a trava
/// ocupada, não faz mais nada: quem a detém sela ao confirmar.
/// </summary>
internal sealed partial class GeoReconciliacaoStartupHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TravaConsultivaDaCargaDne _trava;
    private readonly ArquivoDoPacoteDne _arquivos;
    private readonly ILogger<GeoReconciliacaoStartupHostedService> _logger;

    public GeoReconciliacaoStartupHostedService(
        IServiceScopeFactory scopeFactory,
        TravaConsultivaDaCargaDne trava,
        ArquivoDoPacoteDne arquivos,
        ILogger<GeoReconciliacaoStartupHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(trava);
        ArgumentNullException.ThrowIfNull(arquivos);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _trava = trava;
        _arquivos = arquivos;
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

    private async Task ReconciliarEmEscopoAsync(CancellationToken cancellationToken)
    {
        using IServiceScope escopo = _scopeFactory.CreateScope();
        EncerramentoDasCargasAbandonadas encerramento = escopo.ServiceProvider.GetRequiredService<EncerramentoDasCargasAbandonadas>();
        await TentarEncerrarAsync(encerramento, cancellationToken).ConfigureAwait(false);
        GeoDbContext contexto = escopo.ServiceProvider.GetRequiredService<GeoDbContext>();
        await SincronizarSeloDoCacheAsync(escopo.ServiceProvider, contexto, cancellationToken).ConfigureAwait(false);
    }

    private async Task TentarEncerrarAsync(EncerramentoDasCargasAbandonadas encerramento, CancellationToken cancellationToken)
    {
        try
        {
            await encerramento.EncerrarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            LogErroReconciliar(_logger, DiagnosticoDeExcecaoSanitizado.Descrever(excecao));
        }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: {Removidos} item(ns) residual(is) do diretório de pacotes removido(s) na partida.")]
    private static partial void LogPacotesResiduaisRemovidos(ILogger logger, int removidos);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: falha ao limpar o diretório de pacotes na partida. Diagnóstico: {Diagnostico}")]
    private static partial void LogErroLimparDiretorio(ILogger logger, string diagnostico);

    [LoggerMessage(Level = LogLevel.Information, Message = "Geo: trava consultiva ocupada na partida — nenhuma execução foi alterada e o selo do cache não foi sincronizado.")]
    private static partial void LogTravaOcupadaNaPartida(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: falha ao reconciliar execuções em andamento no startup. Diagnóstico: {Diagnostico}")]
    private static partial void LogErroReconciliar(ILogger logger, string diagnostico);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: falha ao sincronizar o selo do cache para a versão {Versao} na partida. Diagnóstico: {Diagnostico}")]
    private static partial void LogErroSincronizarSelo(ILogger logger, string versao, string diagnostico);
}
