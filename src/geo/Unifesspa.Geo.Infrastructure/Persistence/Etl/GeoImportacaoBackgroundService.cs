namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// Consome a fila de disparos da carga e executa cada carga num escopo de DI próprio
/// (o executor e o <c>GeoDbContext</c> são scoped). A reconciliação de execuções
/// <c>EmAndamento</c> órfãs roda antes deste processo em segundo plano, no
/// <see cref="GeoReconciliacaoStartupHostedService"/>.
/// </summary>
internal sealed partial class GeoImportacaoBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGeoImportacaoFila _fila;
    private readonly ArquivoDoPacoteDne _arquivos;
    private readonly ILogger<GeoImportacaoBackgroundService> _logger;

    public GeoImportacaoBackgroundService(
        IServiceScopeFactory scopeFactory,
        IGeoImportacaoFila fila,
        ArquivoDoPacoteDne arquivos,
        ILogger<GeoImportacaoBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(fila);
        ArgumentNullException.ThrowIfNull(arquivos);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _fila = fila;
        _arquivos = arquivos;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (CargaReservada carga in _fila.LerAsync(stoppingToken).ConfigureAwait(false))
        {
            if (!await ProcessarItemAsync(carga, stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private async Task<bool> ProcessarItemAsync(CargaReservada carga, CancellationToken stoppingToken)
    {
        try
        {
            return await ExecutarEmEscopoAsync(carga, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            LogErroProcessando(_logger, carga.ExecucaoId, DiagnosticoDeExcecaoSanitizado.Descrever(excecao));
            await TentarMarcarFalhaInesperadaAsync(carga.ExecucaoId).ConfigureAwait(false);
            ApagarArquivoTolerandoFalha(carga.ExecucaoId);
            return true;
        }
        finally
        {
            await carga.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task TentarMarcarFalhaInesperadaAsync(Guid execucaoId)
    {
        try
        {
            using IServiceScope escopo = _scopeFactory.CreateScope();
            IGeoImportacaoExecutor executor = escopo.ServiceProvider.GetRequiredService<IGeoImportacaoExecutor>();
            await executor.MarcarFalhaInesperadaAsync(execucaoId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            LogErroMarcarFalha(_logger, execucaoId, DiagnosticoDeExcecaoSanitizado.Descrever(excecao));
        }
    }

    private async Task<bool> ExecutarEmEscopoAsync(CargaReservada carga, CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope escopo = _scopeFactory.CreateScope();
            IGeoImportacaoExecutor executor = escopo.ServiceProvider.GetRequiredService<IGeoImportacaoExecutor>();
            await executor.ExecutarAsync(carga, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// No desligamento, drena a fila, marca <c>Falhou</c> as execuções enfileiradas que
    /// não chegaram a ser processadas e só então solta a trava de cada uma.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        _fila.Completar();
        IReadOnlyList<CargaReservada> pendentes = _fila.DrenarRestante();
        if (pendentes.Count == 0)
        {
            return;
        }

        await FalharPendentesAsync(pendentes, cancellationToken).ConfigureAwait(false);
    }

    private async Task FalharPendentesAsync(IReadOnlyList<CargaReservada> pendentes, CancellationToken cancellationToken)
    {
        try
        {
            await MarcarPendentesComoInterrompidasAsync(pendentes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            LogErroDrenarFila(_logger, DiagnosticoDeExcecaoSanitizado.Descrever(excecao));
        }
        finally
        {
            await SoltarTravasAsync(pendentes).ConfigureAwait(false);
        }
    }

    private async Task SoltarTravasAsync(IReadOnlyList<CargaReservada> pendentes)
    {
        foreach (CargaReservada carga in pendentes)
        {
            ApagarArquivoTolerandoFalha(carga.ExecucaoId);
            await carga.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ApagarArquivoTolerandoFalha(Guid execucaoId)
    {
        try
        {
            _arquivos.ApagarSeExistir(execucaoId);
        }
        catch (Exception excecao) when (excecao is IOException or UnauthorizedAccessException)
        {
            LogErroApagarArquivo(_logger, execucaoId, DiagnosticoDeExcecaoSanitizado.Descrever(excecao));
        }
    }

    private async Task MarcarPendentesComoInterrompidasAsync(IReadOnlyList<CargaReservada> pendentes, CancellationToken cancellationToken)
    {
        using IServiceScope escopo = _scopeFactory.CreateScope();
        IGeoImportacaoExecutor executor = escopo.ServiceProvider.GetRequiredService<IGeoImportacaoExecutor>();

        foreach (CargaReservada carga in pendentes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await executor.MarcarInterrompidaNoDesligamentoAsync(carga.ExecucaoId, cancellationToken).ConfigureAwait(false);
            ApagarArquivoTolerandoFalha(carga.ExecucaoId);
            await carga.DisposeAsync().ConfigureAwait(false);
            LogInterrompidaNoDesligamento(_logger, carga.ExecucaoId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: erro ao processar a execução {ExecucaoId} na fila. Diagnóstico: {Diagnostico}")]
    private static partial void LogErroProcessando(ILogger logger, Guid execucaoId, string diagnostico);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: não foi possível marcar como falha a execução {ExecucaoId} após erro na fila. Diagnóstico: {Diagnostico}")]
    private static partial void LogErroMarcarFalha(ILogger logger, Guid execucaoId, string diagnostico);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: execução {ExecucaoId} enfileirada e não processada foi marcada como falha no desligamento do processo em segundo plano.")]
    private static partial void LogInterrompidaNoDesligamento(ILogger logger, Guid execucaoId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: não foi possível apagar o arquivo do pacote da execução {ExecucaoId}. Diagnóstico: {Diagnostico}")]
    private static partial void LogErroApagarArquivo(ILogger logger, Guid execucaoId, string diagnostico);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: falha ao drenar a fila no desligamento do processo em segundo plano. Diagnóstico: {Diagnostico}")]
    private static partial void LogErroDrenarFila(ILogger logger, string diagnostico);
}
