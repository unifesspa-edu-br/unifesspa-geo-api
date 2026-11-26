namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

using Microsoft.Extensions.Logging;

using Npgsql;

/// <summary>
/// Carga inicial do logradouro: esvazia a tabela, remove os índices pesados (trigrama do nome
/// e espacial da coordenada) para a cópia ser rápida e os recria ao fim com
/// <c>CREATE INDEX CONCURRENTLY</c>, que só funciona fora de transação, na conexão sem
/// transação da <see cref="SessaoDaCargaEmLote"/>. Como cada remoção é permanente, a tabela
/// nunca fica sem os índices: eles são recriados também quando a carga falha ou é cancelada.
/// </summary>
internal sealed partial class CargaInicialDoLogradouro
{
    private const string RemoverIndiceDoNome = "DROP INDEX IF EXISTS ix_logradouro_nome_trgm;";
    private const string RemoverIndiceDaCoordenada = "DROP INDEX IF EXISTS ix_logradouro_coordenada;";
    private const string CriarIndiceDoNome = "CREATE INDEX CONCURRENTLY ix_logradouro_nome_trgm ON logradouro USING gin (nome_normalizado gin_trgm_ops);";
    private const string CriarIndiceDaCoordenada = "CREATE INDEX CONCURRENTLY ix_logradouro_coordenada ON logradouro USING gist (coordenada);";

    private readonly SessaoDaCargaEmLote _sessao;
    private readonly ILogger _logger;

    public CargaInicialDoLogradouro(SessaoDaCargaEmLote sessao, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(sessao);
        ArgumentNullException.ThrowIfNull(logger);

        _sessao = sessao;
        _logger = logger;
    }

    // O esvaziamento fica fora da proteção: se falhar, nenhum índice foi removido ainda e não há
    // o que recuperar. Esvaziar torna a repetição da carga inicial repetível, sem violar a chave
    // única por estado parcial anterior.
    public async Task ExecutarAsync(Func<Task> carga, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(carga);

        await _sessao.ExecutarAsync("TRUNCATE TABLE logradouro;", cancellationToken).ConfigureAwait(false);
        await CarregarSemOsIndicesAsync(carga, cancellationToken).ConfigureAwait(false);
        await RecriarIndicesAsync().ConfigureAwait(false);
    }

    // A remoção dos índices fica dentro da proteção: cada uma é permanente, então uma falha entre
    // as duas, ou depois delas, também precisa recriar.
    private async Task CarregarSemOsIndicesAsync(Func<Task> carga, CancellationToken cancellationToken)
    {
        try
        {
            await _sessao.ExecutarAsync(RemoverIndiceDoNome, cancellationToken).ConfigureAwait(false);
            await _sessao.ExecutarAsync(RemoverIndiceDaCoordenada, cancellationToken).ConfigureAwait(false);
            await carga().ConfigureAwait(false);
        }
        catch
        {
            await RecriarSemEncobrirAFalhaAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Recria sem o token de cancelamento: cada índice é removido logo antes de ser recriado, e
    // interromper no meio deixaria a tabela sem um índice pesado. A remoção antes de cada criação
    // também limpa um índice inválido deixado por uma recriação anterior interrompida.
    private async Task RecriarIndicesAsync()
    {
        await _sessao.ExecutarAsync(RemoverIndiceDoNome, CancellationToken.None).ConfigureAwait(false);
        await _sessao.ExecutarAsync(CriarIndiceDoNome, CancellationToken.None).ConfigureAwait(false);
        await _sessao.ExecutarAsync(RemoverIndiceDaCoordenada, CancellationToken.None).ConfigureAwait(false);
        await _sessao.ExecutarAsync(CriarIndiceDaCoordenada, CancellationToken.None).ConfigureAwait(false);
    }

    // Depois de uma falha, o erro da recriação só vai ao log para não esconder o erro original
    // da carga; repetir a carga inicial restaura os índices.
    private async Task RecriarSemEncobrirAFalhaAsync()
    {
        try
        {
            await RecriarIndicesAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            LogRecriacaoIndicesFalhou(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "ETL Geo: falha ao recriar índices pesados de logradouro após carga inicial abortada — reexecute o modo Inicial para restaurá-los.")]
    private static partial void LogRecriacaoIndicesFalhou(ILogger logger, Exception excecao);
}
