namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// Encerra como falha as cargas que ficaram registradas como em andamento sem que nenhuma instância
/// as execute. A trava consultiva da carga indica que há alguém executando: com a trava livre, toda
/// execução em andamento é tratada como abandonada, sem critério de tempo. Se o banco soltou a trava com
/// a carga ainda viva, a conclusão dela é recusada. Antes de encerrá-las, descarta os esquemas
/// temporários de carga que sobraram.
/// </summary>
internal sealed partial class EncerramentoDasCargasAbandonadas
{
    internal const string MensagemDaCargaAbandonada = "Carga interrompida antes de concluir.";

    private readonly GeoDbContext _contexto;
    private readonly IFonteDaExecucaoDne _fonteDaExecucao;
    private readonly TravaConsultivaDaCargaDne _trava;
    private readonly TimeProvider _relogio;
    private readonly ILogger<EncerramentoDasCargasAbandonadas> _logger;

    public EncerramentoDasCargasAbandonadas(
        GeoDbContext contexto,
        IFonteDaExecucaoDne fonteDaExecucao,
        TravaConsultivaDaCargaDne trava,
        TimeProvider relogio,
        ILogger<EncerramentoDasCargasAbandonadas> logger)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(fonteDaExecucao);
        ArgumentNullException.ThrowIfNull(trava);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(logger);

        _contexto = contexto;
        _fonteDaExecucao = fonteDaExecucao;
        _trava = trava;
        _relogio = relogio;
        _logger = logger;
    }

    /// <summary>
    /// Diz se há carga sendo executada por alguma instância. Sem execução em andamento registrada,
    /// não consulta a trava. Com registro e trava livre, encerra as abandonadas e devolve falso.
    /// </summary>
    public async Task<bool> HaCargaEmExecucaoAsync(CancellationToken cancellationToken)
    {
        bool registrada = await ExisteEmAndamentoAsync(cancellationToken).ConfigureAwait(false);
        return registrada && !await EncerrarSeATravaEstiverLivreAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Descarta os esquemas de carga que sobraram e, só depois, marca como falha toda execução em
    /// andamento: se o descarte falhar ou for cancelado, a exceção sobe e as execuções seguem em
    /// andamento, para nova tentativa no próximo envio ou na próxima partida. Quem chama já segura
    /// a trava consultiva da carga. Devolve quantas execuções encerrou; se a carga terminar antes da
    /// gravação, a execução já não está em andamento, não recebe falha e não entra na conta.
    /// </summary>
    public async Task<int> EncerrarAsync(CancellationToken cancellationToken)
    {
        await DescartarSobrasAsync(cancellationToken).ConfigureAwait(false);
        return await FalharEmAndamentoAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<bool> ExisteEmAndamentoAsync(CancellationToken cancellationToken) =>
        _contexto.ImportacaoExecucoes
            .AsNoTracking()
            .AnyAsync(e => e.Status == StatusImportacao.EmAndamento, cancellationToken);

    private async Task<bool> EncerrarSeATravaEstiverLivreAsync(CancellationToken cancellationToken)
    {
        IAsyncDisposable? trava = await _trava.TentarAdquirirAsync(cancellationToken).ConfigureAwait(false);
        if (trava is null)
        {
            return false;
        }

        await using (trava.ConfigureAwait(false))
        {
            await EncerrarAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<int> FalharEmAndamentoAsync(CancellationToken cancellationToken)
    {
        List<GeoImportacaoExecucao> abandonadas = await _contexto.ImportacaoExecucoes
            .Where(e => e.Status == StatusImportacao.EmAndamento)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return abandonadas.Count == 0
            ? 0
            : await GravarFalhaAsync(abandonadas, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> GravarFalhaAsync(List<GeoImportacaoExecucao> abandonadas, CancellationToken cancellationToken)
    {
        DateTimeOffset agora = _relogio.GetUtcNow();
        foreach (GeoImportacaoExecucao execucao in abandonadas)
        {
            execucao.Falhar(agora, MensagemDaCargaAbandonada);
        }

        if (!await TentarGravarAsync(cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        LogCargasAbandonadasEncerradas(_logger, abandonadas.Count);
        return abandonadas.Count;
    }

    // Se a carga gravou a conclusão entre a leitura e esta gravação, a situação lida já não vale: a
    // execução terminou e não recebe falha.
    private async Task<bool> TentarGravarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _contexto.ChangeTracker.Clear();
            LogCargaTerminouAntesDoEncerramento(_logger);
            return false;
        }
    }

    private async Task DescartarSobrasAsync(CancellationToken cancellationToken)
    {
        int descartados = await _fonteDaExecucao.DescartarSobrasAsync(cancellationToken).ConfigureAwait(false);
        if (descartados > 0)
        {
            LogEsquemasDescartados(_logger, descartados);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: {Encerradas} carga(s) abandonada(s) em andamento encerrada(s) como falha.")]
    private static partial void LogCargasAbandonadasEncerradas(ILogger logger, int encerradas);

    [LoggerMessage(Level = LogLevel.Information, Message = "Geo: a carga em andamento terminou antes do encerramento; nenhuma falha foi gravada.")]
    private static partial void LogCargaTerminouAntesDoEncerramento(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: {Descartados} esquema(s) de carga que sobrou(aram) descartado(s).")]
    private static partial void LogEsquemasDescartados(ILogger logger, int descartados);
}
