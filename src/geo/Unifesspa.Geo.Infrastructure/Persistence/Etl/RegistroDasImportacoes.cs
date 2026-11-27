namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using System.Data.Common;
using System.Text.Json;
using System.Threading.Channels;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Porta da API para a carga de atualização periódica do Geo: valida o disparo, obtém a trava
/// consultiva da carga, aplica a guarda de versão progressiva, registra e enfileira a execução
/// (a trava segue com ela até o fim da carga) e expõe a consulta de estado.
/// </summary>
internal sealed partial class RegistroDasImportacoes : IGeoImportacaoService
{
    private const string PostgresUniqueViolation = "23505";

    private static readonly JsonSerializerOptions RelatorioJsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly GeoDbContext _contexto;
    private readonly IGeoImportacaoFila _fila;
    private readonly TravaConsultivaDaCargaDne _trava;
    private readonly TimeProvider _relogio;
    private readonly ILogger<RegistroDasImportacoes> _logger;

    public RegistroDasImportacoes(
        GeoDbContext contexto,
        IGeoImportacaoFila fila,
        TravaConsultivaDaCargaDne trava,
        TimeProvider relogio,
        ILogger<RegistroDasImportacoes> logger)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(fila);
        ArgumentNullException.ThrowIfNull(trava);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(logger);

        _contexto = contexto;
        _fila = fila;
        _trava = trava;
        _relogio = relogio;
        _logger = logger;
    }

    public async Task<Result<Guid>> IniciarAsync(string versao, string disparadoPor, CancellationToken cancellationToken)
    {
        Result<GeoImportacaoExecucao> criacao = GeoImportacaoExecucao.Iniciar(versao, disparadoPor, _relogio.GetUtcNow());
        if (criacao.IsFailure)
        {
            return Result<Guid>.Failure(criacao.Error!);
        }

        IAsyncDisposable? trava = await _trava.TentarAdquirirAsync(cancellationToken).ConfigureAwait(false);
        if (trava is null)
        {
            LogConflito(_logger, criacao.Value!.VersaoDataset);
            return Result<Guid>.Failure(ErroEmAndamento());
        }

        return await RegistrarSobTravaAsync(criacao.Value!, trava, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ImportacaoGeoDto?> ObterAsync(Guid id, CancellationToken cancellationToken)
    {
        GeoImportacaoExecucao? execucao = await _contexto.ImportacaoExecucoes
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return execucao is null ? null : MapearDto(execucao);
    }

    private async Task<Result<Guid>> RegistrarSobTravaAsync(GeoImportacaoExecucao execucao, IAsyncDisposable trava, CancellationToken cancellationToken)
    {
        CargaReservada? carga = CargaReservada.Reservar(execucao.Id, trava);
        try
        {
            Result<Guid> registro = await VerificarERegistrarAsync(execucao, carga, cancellationToken).ConfigureAwait(false);
            carga = registro.IsSuccess ? null : carga;
            return registro;
        }
        finally
        {
            await SoltarSeNaoEntregueAsync(carga).ConfigureAwait(false);
        }
    }

    private static ValueTask SoltarSeNaoEntregueAsync(CargaReservada? carga) =>
        carga is null || carga.EntregueAFila ? ValueTask.CompletedTask : carga.DisposeAsync();

    private async Task<Result<Guid>> VerificarERegistrarAsync(
        GeoImportacaoExecucao execucao,
        CargaReservada carga,
        CancellationToken cancellationToken)
    {
        Result guarda = await VerificarVersaoProgressivaAsync(execucao.VersaoDataset, cancellationToken).ConfigureAwait(false);
        if (guarda.IsFailure)
        {
            return Result<Guid>.Failure(guarda.Error!);
        }

        return await PersistirEEnfileirarAsync(execucao, carga, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> VerificarVersaoProgressivaAsync(string versao, CancellationToken cancellationToken)
    {
        string? ultimaAplicada = await ObterUltimaVersaoAplicadaAsync(cancellationToken).ConfigureAwait(false);
        if (ultimaAplicada is not null && string.CompareOrdinal(versao, ultimaAplicada) < 0)
        {
            LogVersaoNaoProgressiva(_logger, versao, ultimaAplicada);
            return Result.Failure(new DomainError(
                GeoImportacaoErrorCodes.VersaoNaoProgressiva,
                $"A versão {versao} é anterior à última aplicada ({ultimaAplicada})."));
        }

        return Result.Success();
    }

    private async Task<string?> ObterUltimaVersaoAplicadaAsync(CancellationToken cancellationToken)
    {
        string? versaoDados = await _contexto.Cidades
            .MaxAsync(c => (string?)c.VersaoDataset, cancellationToken)
            .ConfigureAwait(false);
        string? versaoHistorico = await _contexto.ImportacaoExecucoes
            .Where(e => e.Status == StatusImportacao.Concluida)
            .MaxAsync(e => (string?)e.VersaoDataset, cancellationToken)
            .ConfigureAwait(false);

        return MaiorVersao(versaoDados, versaoHistorico);
    }

    private async Task<Result<Guid>> PersistirEEnfileirarAsync(
        GeoImportacaoExecucao execucao,
        CargaReservada carga,
        CancellationToken cancellationToken)
    {
        if (!await TentarPersistirAsync(execucao, cancellationToken).ConfigureAwait(false))
        {
            return Result<Guid>.Failure(ErroEmAndamento());
        }

        return await TentarEnfileirarAsync(execucao, carga).ConfigureAwait(false);
    }

    private static DomainError ErroEmAndamento() =>
        new(GeoImportacaoErrorCodes.ImportacaoEmAndamento, "Já existe uma importação do Geo em andamento.");

    private async Task<bool> TentarPersistirAsync(GeoImportacaoExecucao execucao, CancellationToken cancellationToken)
    {
        _contexto.ImportacaoExecucoes.Add(execucao);
        try
        {
            await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (EhViolacaoUnica(ex))
        {
            _contexto.ImportacaoExecucoes.Entry(execucao).State = EntityState.Detached;
            LogConflito(_logger, execucao.VersaoDataset);
            return false;
        }
    }

    private async Task<Result<Guid>> TentarEnfileirarAsync(GeoImportacaoExecucao execucao, CargaReservada carga)
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
            return Result<Guid>.Failure(new DomainError(
                GeoImportacaoErrorCodes.NaoEnfileirada,
                "Não foi possível enfileirar a importação (serviço em desligamento)."));
        }

        LogDisparada(_logger, execucao.Id, execucao.VersaoDataset);
        return Result<Guid>.Success(execucao.Id);
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

    private static string? MaiorVersao(string? a, string? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        return string.CompareOrdinal(a, b) >= 0 ? a : b;
    }

    private static bool EhViolacaoUnica(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresUniqueViolation;

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
            relatorio);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Geo: carga {ExecucaoId} disparada (versão {Versao}).")]
    private static partial void LogDisparada(ILogger logger, Guid execucaoId, string versao);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: disparo recusado (versão {Versao}) — já há uma carga em andamento.")]
    private static partial void LogConflito(ILogger logger, string versao);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: execução {ExecucaoId} não enfileirada (serviço em desligamento). Diagnóstico: {Diagnostico}")]
    private static partial void LogNaoEnfileirada(ILogger logger, Guid execucaoId, string diagnostico);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: disparo recusado — versão {Versao} anterior à última aplicada {UltimaVersao}.")]
    private static partial void LogVersaoNaoProgressiva(ILogger logger, string versao, string ultimaVersao);

    [LoggerMessage(Level = LogLevel.Error, Message = "Geo: falha ao persistir o estado de falha da execução {ExecucaoId}. Diagnóstico: {Diagnostico}")]
    private static partial void LogFalhaPersistir(ILogger logger, Guid execucaoId, string diagnostico);
}
