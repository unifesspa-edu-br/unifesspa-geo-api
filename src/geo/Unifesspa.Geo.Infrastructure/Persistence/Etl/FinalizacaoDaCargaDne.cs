namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Observability;

/// <summary>
/// Trata a finalização, o selo do cache de CEP, a verificação de confirmação incerta
/// em escopo isolado, a marcação de falha pela entidade e as métricas da carga DNE.
/// </summary>
internal sealed partial class FinalizacaoDaCargaDne
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Lazy<IGeoCepCacheInvalidador> _cacheInvalidador;
    private readonly GeoEtlMetrics _metricas;
    private readonly TimeProvider _relogio;
    private readonly ILogger<FinalizacaoDaCargaDne> _logger;

    public FinalizacaoDaCargaDne(
        IServiceScopeFactory scopeFactory,
        Lazy<IGeoCepCacheInvalidador> cacheInvalidador,
        GeoEtlMetrics metricas,
        TimeProvider relogio,
        ILogger<FinalizacaoDaCargaDne> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(cacheInvalidador);
        ArgumentNullException.ThrowIfNull(metricas);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _cacheInvalidador = cacheInvalidador;
        _metricas = metricas;
        _relogio = relogio;
        _logger = logger;
    }

    public async Task ConcluirComSeloAsync(Guid execucaoId, string versao, long inicioTimestamp, RelatorioImportacaoDto relatorio)
    {
        await SelarCacheVigenteAsync(versao, execucaoId).ConfigureAwait(false);
        double duracaoMs = _relogio.GetElapsedTime(inicioTimestamp).TotalMilliseconds;
        _metricas.RegistrarConclusao(versao, duracaoMs, relatorio.Inseridos + relatorio.Atualizados, relatorio.Degradados);
        LogConcluida(_logger, execucaoId, versao, relatorio.Inseridos, relatorio.Atualizados, relatorio.Degradados);
    }

    public async Task TratarCommitIncertoAsync(
        Guid execucaoId,
        string versao,
        long inicioTimestamp,
        RelatorioImportacaoDto relatorio,
        Exception excecaoCommit)
    {
        string diagnostico = DiagnosticoDeExcecaoSanitizado.Descrever(excecaoCommit);
        try
        {
            await InspecionarEFinalizarCommitIncertoAsync(execucaoId, versao, inicioTimestamp, relatorio, diagnostico).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            RegistrarFalhaCommitIncerto(execucaoId, versao, inicioTimestamp, ex);
        }
    }

    public async Task MarcarFalhaAsync(Guid execucaoId, string mensagem, CancellationToken cancellationToken)
    {
        try
        {
            await PersistirFalhaEmEscopoAsync(execucaoId, mensagem, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            LogFalhaPersistir(_logger, execucaoId, DiagnosticoDeExcecaoSanitizado.Descrever(excecao));
        }
    }

    public void RegistrarMetricaFalha(string versao, long inicioTimestamp) =>
        _metricas.RegistrarFalha(versao, _relogio.GetElapsedTime(inicioTimestamp).TotalMilliseconds);

    private async Task PersistirFalhaEmEscopoAsync(Guid execucaoId, string mensagem, CancellationToken cancellationToken)
    {
        using IServiceScope escopo = _scopeFactory.CreateScope();
        GeoDbContext contexto = escopo.ServiceProvider.GetRequiredService<GeoDbContext>();
        GeoImportacaoExecucao? execucao = await contexto.ImportacaoExecucoes
            .FirstOrDefaultAsync(e => e.Id == execucaoId, cancellationToken)
            .ConfigureAwait(false);

        if (execucao is not null && execucao.Status == StatusImportacao.EmAndamento)
        {
            execucao.Falhar(_relogio.GetUtcNow(), mensagem);
            await contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task InspecionarEFinalizarCommitIncertoAsync(
        Guid execucaoId,
        string versao,
        long inicioTimestamp,
        RelatorioImportacaoDto relatorio,
        string diagnostico)
    {
        using IServiceScope escopo = _scopeFactory.CreateScope();
        GeoDbContext contexto = escopo.ServiceProvider.GetRequiredService<GeoDbContext>();
        GeoImportacaoExecucao? atual = await contexto.ImportacaoExecucoes
            .FirstOrDefaultAsync(e => e.Id == execucaoId, CancellationToken.None)
            .ConfigureAwait(false);

        if (atual is not null && atual.Status == StatusImportacao.Concluida)
        {
            LogCommitIncertoEfetivado(_logger, execucaoId, versao, diagnostico);
            await ConcluirComSeloAsync(execucaoId, versao, inicioTimestamp, relatorio).ConfigureAwait(false);
            return;
        }

        await FalharCommitIncertoRevertidoAsync(contexto, atual, execucaoId, versao, inicioTimestamp, diagnostico).ConfigureAwait(false);
    }

    private async Task FalharCommitIncertoRevertidoAsync(
        GeoDbContext contexto,
        GeoImportacaoExecucao? atual,
        Guid execucaoId,
        string versao,
        long inicioTimestamp,
        string diagnostico)
    {
        if (atual is not null && atual.Status == StatusImportacao.EmAndamento)
        {
            atual.Falhar(_relogio.GetUtcNow(), "Falha na confirmação da transação da carga DNE.");
            await contexto.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        RegistrarMetricaFalha(versao, inicioTimestamp);
        LogCommitIncertoRevertido(_logger, execucaoId, versao, diagnostico);
    }

    private void RegistrarFalhaCommitIncerto(Guid execucaoId, string versao, long inicioTimestamp, Exception ex)
    {
        RegistrarMetricaFalha(versao, inicioTimestamp);
        LogFalhaPersistir(_logger, execucaoId, DiagnosticoDeExcecaoSanitizado.Descrever(ex));
    }

    private async Task SelarCacheVigenteAsync(string versao, Guid execucaoId)
    {
        try
        {
            await _cacheInvalidador.Value.InvalidarAsync(SeloDoCacheDeCep.Identificar(versao, execucaoId), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            LogFalhaSeloCache(_logger, versao, DiagnosticoDeExcecaoSanitizado.Descrever(excecao));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Geo: carga {ExecucaoId} concluída (versão {Versao}, inseridos={Inseridos}, atualizados={Atualizados}, degradados={Degradados}).")]
    private static partial void LogConcluida(ILogger logger, Guid execucaoId, string versao, int inseridos, int atualizados, int degradados);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: falha ao selar a versão vigente {Versao} no cache (sem garantia de sucesso; a carga já foi concluída). Diagnóstico: {Diagnostico}")]
    private static partial void LogFalhaSeloCache(ILogger logger, string versao, string diagnostico);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: falha ao persistir o estado de falha da execução {ExecucaoId}. Diagnóstico: {Diagnostico}")]
    private static partial void LogFalhaPersistir(ILogger logger, Guid execucaoId, string diagnostico);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: erro ao confirmar transação da carga {ExecucaoId} (versão {Versao}), mas a execução já consta concluída no banco. Diagnóstico: {Diagnostico}")]
    private static partial void LogCommitIncertoEfetivado(ILogger logger, Guid execucaoId, string versao, string diagnostico);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: erro ao confirmar transação da carga {ExecucaoId} (versão {Versao}); execução marcada como falha. Diagnóstico: {Diagnostico}")]
    private static partial void LogCommitIncertoRevertido(ILogger logger, Guid execucaoId, string versao, string diagnostico);
}
