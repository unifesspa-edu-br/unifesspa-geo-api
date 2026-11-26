namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

using Microsoft.Extensions.Logging;

using Npgsql;

/// <summary>
/// Carrega uma tabela por cópia binária: cria a tabela temporária, copia a fonte em lotes,
/// mescla no destino contando o resultado e remove a temporária. A mesclagem deduplica a
/// própria fonte (<c>DISTINCT ON</c> pela chave natural) e atualiza por <c>ON CONFLICT</c>, o
/// que torna a carga repetível. A cópia e a mesclagem não passam pelo interceptor de
/// auditoria, então o <c>created_at</c> e o <c>updated_at</c> recebem o instante único da carga.
/// </summary>
internal sealed partial class CargaDeTabelaPorCopia
{
    // Limita a memória por cópia e dá granularidade ao progresso. A tabela temporária acumula
    // todos os lotes; a mesclagem final é uma única instrução.
    private const int LinhasPorLote = 50_000;

    private readonly SessaoDaCargaEmLote _sessao;
    private readonly DateTimeOffset _instanteDaCarga;
    private readonly ILogger _logger;

    public CargaDeTabelaPorCopia(SessaoDaCargaEmLote sessao, DateTimeOffset instanteDaCarga, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(sessao);
        ArgumentNullException.ThrowIfNull(logger);

        _sessao = sessao;
        _instanteDaCarga = instanteDaCarga;
        _logger = logger;
    }

    public async Task CarregarAsync<T>(ITabelaCarregadaPorCopia<T> tabela, RelatorioImportacao relatorio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        ArgumentNullException.ThrowIfNull(relatorio);

        ComandosDaTabelaPorCopia comandos = tabela.Comandos;
        ContadorTabela contador = relatorio.Tabela(comandos.TabelaDeDestino);
        await _sessao.ExecutarAsync(comandos.CriacaoDaTabelaTemporaria, cancellationToken).ConfigureAwait(false);
        long copiadas = await CopiarEmLotesAsync(tabela, contador, cancellationToken).ConfigureAwait(false);
        await MesclarContandoAsync(comandos, contador, copiadas, cancellationToken).ConfigureAwait(false);
        await _sessao.ExecutarAsync(comandos.DescarteDaTabelaTemporaria, cancellationToken).ConfigureAwait(false);
    }

    // Um único enumerador da fonte atravessa todos os lotes: a leitura é contínua e cada lote
    // abre a própria cópia, confirmada ao fim dele.
    private async Task<long> CopiarEmLotesAsync<T>(ITabelaCarregadaPorCopia<T> tabela, ContadorTabela contador, CancellationToken cancellationToken)
    {
        IAsyncEnumerator<T> linhas = tabela.ProjetarAsync(contador, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using (linhas.ConfigureAwait(false))
        {
            return await CopiarLotesAteAcabarAsync(tabela, linhas, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<long> CopiarLotesAteAcabarAsync<T>(ITabelaCarregadaPorCopia<T> tabela, IAsyncEnumerator<T> linhas, CancellationToken cancellationToken)
    {
        long total = 0;
        int copiadasNoLote = await CopiarLoteAsync(tabela, linhas, cancellationToken).ConfigureAwait(false);
        while (copiadasNoLote > 0)
        {
            total += copiadasNoLote;
            LogLoteConcluido(_logger, tabela.Comandos.TabelaDeDestino, total);
            copiadasNoLote = await CopiarLoteAsync(tabela, linhas, cancellationToken).ConfigureAwait(false);
        }

        return total;
    }

    // Só abre a cópia se houver ao menos uma linha: fonte vazia, ou que acabou num lote exatamente
    // cheio, não gera cópia vazia.
    private async Task<int> CopiarLoteAsync<T>(ITabelaCarregadaPorCopia<T> tabela, IAsyncEnumerator<T> linhas, CancellationToken cancellationToken)
    {
        if (!await linhas.MoveNextAsync().ConfigureAwait(false))
        {
            return 0;
        }

        NpgsqlBinaryImporter copia = await _sessao.IniciarCopiaAsync(tabela.Comandos.Copia, cancellationToken).ConfigureAwait(false);
        await using (copia.ConfigureAwait(false))
        {
            int escritas = await EscreverLinhasDoLoteAsync(tabela, linhas, copia, cancellationToken).ConfigureAwait(false);
            await copia.CompleteAsync(cancellationToken).ConfigureAwait(false);
            return escritas;
        }
    }

    // Parte da linha já lida por CopiarLoteAsync e para ao completar o lote, sem consumir a
    // linha seguinte, que pertence ao próximo.
    private async Task<int> EscreverLinhasDoLoteAsync<T>(ITabelaCarregadaPorCopia<T> tabela, IAsyncEnumerator<T> linhas, NpgsqlBinaryImporter copia, CancellationToken cancellationToken)
    {
        int escritas = 0;
        do
        {
            await copia.StartRowAsync(cancellationToken).ConfigureAwait(false);
            await tabela.EscreverLinhaAsync(copia, linhas.Current, _instanteDaCarga, cancellationToken).ConfigureAwait(false);
            escritas++;
        }
        while (escritas < LinhasPorLote && await linhas.MoveNextAsync().ConfigureAwait(false));

        return escritas;
    }

    private async Task MesclarContandoAsync(ComandosDaTabelaPorCopia comandos, ContadorTabela contador, long copiadas, CancellationToken cancellationToken)
    {
        long antes = await _sessao.ContarLinhasAsync(comandos.TabelaDeDestino, cancellationToken).ConfigureAwait(false);
        long afetadas = await _sessao.MesclarAsync(comandos.Mesclagem, _instanteDaCarga, cancellationToken).ConfigureAwait(false);
        long depois = await _sessao.ContarLinhasAsync(comandos.TabelaDeDestino, cancellationToken).ConfigureAwait(false);
        contador.ContarMesclagem(copiadas, afetadas, depois - antes);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ETL Geo: COPY {Tabela} — {LinhasAcumuladas} linhas enviadas à tabela temporária.")]
    private static partial void LogLoteConcluido(ILogger logger, string tabela, long linhasAcumuladas);
}
