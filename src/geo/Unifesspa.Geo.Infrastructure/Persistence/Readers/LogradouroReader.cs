namespace Unifesspa.Geo.Infrastructure.Persistence.Readers;

using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Core.Pagination;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// Leitor read-side de <see cref="Logradouro"/> sobre o <see cref="GeoDbContext"/>,
/// sempre restrito a uma Cidade vigente e com busca textual opcional.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via DI em GeoInfrastructureRegistration.")]
internal sealed class LogradouroReader : ILogradouroReader
{
    private readonly GeoDbContext _dbContext;

    public LogradouroReader(GeoDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<(bool CidadeExiste, IReadOnlyList<LogradouroComBairro> Itens, Guid? AnteriorAfterId, Guid? ProximoAfterId)> ListarPorCidadeAsync(
        string codigoIbge,
        Guid? afterId,
        int limit,
        PaginationDirection direction,
        string? busca,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigoIbge);

        if (await _dbContext.IdDaCidadeVigenteAsync(codigoIbge, cancellationToken).ConfigureAwait(false) is not { } cidadeId)
        {
            return (false, [], null, null);
        }

        IQueryable<Logradouro> escopoCidade = _dbContext.Logradouros
            .AsNoTracking()
            .Where(l => l.Vigente && l.CidadeId == cidadeId);

        return string.IsNullOrWhiteSpace(busca)
            ? await ListarNavegandoAsync(escopoCidade, afterId, limit, direction, cancellationToken).ConfigureAwait(false)
            : await BuscarPorRelevanciaAsync(escopoCidade, busca, limit, cancellationToken).ConfigureAwait(false);
    }

    // Navegação (sem busca): lista da cidade paginada por cursor keyset bidirecional
    // (ADR-0089), ordenada por Id.
    private async Task<(bool, IReadOnlyList<LogradouroComBairro>, Guid?, Guid?)> ListarNavegandoAsync(
        IQueryable<Logradouro> escopoCidade,
        Guid? afterId,
        int limit,
        PaginationDirection direction,
        CancellationToken cancellationToken)
    {
        CursorKeysetPage<Logradouro> page = await CursorKeyset
            .ApplyAsync(escopoCidade, afterId, limit, direction, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<LogradouroComBairro> itens = await EnriquecerComBairroAsync(page.Items, cancellationToken)
            .ConfigureAwait(false);

        return (true, itens, page.PrevAfterId, page.NextAfterId);
    }

    // Autocomplete (com busca): filtra por word_similarity (pg_trgm, operador `<%`) e
    // ordena por relevância. Ao contrário do ILIKE (substring contígua, frágil), o
    // word_similarity tolera abreviação de tipo ("av brasil" → "Avenida Brasil"), ordem
    // das palavras ("paulista avenida") e pequenos erros de digitação — sem dicionário de
    // abreviações. O operador `<%` reaproveita o índice gin_trgm_ops
    // (ix_logradouro_nome_trgm) — sem migration. O ranking
    // `word_similarity DESC, similarity DESC` coloca a match exata no topo: o desempate
    // por `similarity` global separa "Rua das Flores" de "Rua das Flores e Jardins", que
    // empatam em word_similarity = 1. Typo puro tem recall ok mas ranking subótimo —
    // limitação aceita para autocomplete.
    //
    // O ranking por similaridade é incompatível com o keyset por Id (ADR-0089), então
    // retorna o top-N sem âncoras de paginação (sem prev/next): a navegação profunda não
    // faz sentido para autocomplete.
    private async Task<(bool, IReadOnlyList<LogradouroComBairro>, Guid?, Guid?)> BuscarPorRelevanciaAsync(
        IQueryable<Logradouro> escopoCidade,
        string busca,
        int limit,
        CancellationToken cancellationToken)
    {
        string termo = BuscaTextualNormalizada.Normalizar(busca);

        List<Logradouro> rankeados = await ListarComLimiarDeSimilaridadeAsync(
            RanqueadosPorSimilaridade(escopoCidade, termo, limit),
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<LogradouroComBairro> itens = await EnriquecerComBairroAsync(rankeados, cancellationToken)
            .ConfigureAwait(false);

        return (true, itens, null, null);
    }

    private static IQueryable<Logradouro> RanqueadosPorSimilaridade(IQueryable<Logradouro> escopoCidade, string termo, int limit) =>
        escopoCidade
            .Where(l => EF.Functions.TrigramsAreWordSimilar(termo, l.NomeNormalizado))
            .OrderByDescending(l => EF.Functions.TrigramsWordSimilarity(termo, l.NomeNormalizado))
            .ThenByDescending(l => EF.Functions.TrigramsSimilarity(l.NomeNormalizado, termo))
            .ThenBy(l => l.Id)
            .Take(limit);

    // O filtro `<%` decide o match comparando pg_trgm.word_similarity_threshold, lido
    // em tempo de execução. SET LOCAL o fixa em 0.6 (default do PostgreSQL; calibrado
    // contra o DNE real — mantém a match esperada em 1º com baixo volume de
    // falsos positivos) de forma transação-local: determinístico, sem depender da
    // config global do servidor e sem vazar para a conexão devolvida ao pool. SET LOCAL
    // exige um bloco de transação. No caminho de query atual o handler não roda sob
    // transação ambiente (verificado nos testes de integração, que sobem o runtime
    // Wolverine real), então abre a sua própria; se uma transação ambiente já existir,
    // reaproveita-a sem assumir seu ciclo de vida — evita abrir uma transação aninhada
    // e blinda contra mudança futura no pipeline.
    private async Task<List<Logradouro>> ListarComLimiarDeSimilaridadeAsync(
        IQueryable<Logradouro> consulta,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            return await FixarLimiarEListarAsync(consulta, cancellationToken).ConfigureAwait(false);
        }

        IDbContextTransaction transacao = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (transacao.ConfigureAwait(false))
        {
            List<Logradouro> listados = await FixarLimiarEListarAsync(consulta, cancellationToken).ConfigureAwait(false);
            await transacao.CommitAsync(cancellationToken).ConfigureAwait(false);
            return listados;
        }
    }

    private async Task<List<Logradouro>> FixarLimiarEListarAsync(
        IQueryable<Logradouro> consulta,
        CancellationToken cancellationToken)
    {
        // String literal constante (sem interpolação) — não dispara o EF1002.
        await _dbContext.Database
            .ExecuteSqlRawAsync("SET LOCAL pg_trgm.word_similarity_threshold = 0.6", cancellationToken)
            .ConfigureAwait(false);

        return await consulta.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<LogradouroComBairro>> EnriquecerComBairroAsync(
        IReadOnlyList<Logradouro> logradouros,
        CancellationToken cancellationToken)
    {
        if (logradouros.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, string> bairros = await NomesDosBairrosVigentesAsync(logradouros, cancellationToken)
            .ConfigureAwait(false);

        return [.. logradouros.Select(l =>
            new LogradouroComBairro(
                l,
                l.BairroId is { } bairroId && bairros.TryGetValue(bairroId, out string? nome) ? nome : null))];
    }

    private async Task<Dictionary<Guid, string>> NomesDosBairrosVigentesAsync(
        IReadOnlyList<Logradouro> logradouros,
        CancellationToken cancellationToken)
    {
        Guid[] bairroIds = [.. logradouros
            .Select(l => l.BairroId)
            .OfType<Guid>()
            .Distinct()];

        return bairroIds.Length == 0
            ? []
            : await _dbContext.Bairros
                .AsNoTracking()
                .Where(b => b.Vigente && bairroIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Nome, cancellationToken)
                .ConfigureAwait(false);
    }
}
