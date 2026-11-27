namespace Unifesspa.Geo.Infrastructure.Persistence.Readers;

using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Core.Pagination;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// Leitor read-side de <see cref="Bairro"/> sobre o <see cref="GeoDbContext"/>,
/// sempre restrito a uma Cidade vigente e com busca textual opcional.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via DI em GeoInfrastructureRegistration.")]
internal sealed class BairroReader : IBairroReader
{
    private readonly GeoDbContext _dbContext;

    public BairroReader(GeoDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<(bool CidadeExiste, IReadOnlyList<Bairro> Itens, Guid? AnteriorAfterId, Guid? ProximoAfterId)> ListarPorCidadeAsync(
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

        return await ListarDaCidadeAsync(cidadeId, afterId, limit, direction, busca, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(bool, IReadOnlyList<Bairro>, Guid?, Guid?)> ListarDaCidadeAsync(
        Guid cidadeId,
        Guid? afterId,
        int limit,
        PaginationDirection direction,
        string? busca,
        CancellationToken cancellationToken)
    {
        IQueryable<Bairro> query = ComNomeQueContem(
            _dbContext.Bairros
                .AsNoTracking()
                .Where(b => b.Vigente && b.CidadeId == cidadeId),
            busca);

        CursorKeysetPage<Bairro> page = await CursorKeyset
            .ApplyAsync(query, afterId, limit, direction, cancellationToken)
            .ConfigureAwait(false);

        return (true, page.Items, page.PrevAfterId, page.NextAfterId);
    }

    private static IQueryable<Bairro> ComNomeQueContem(IQueryable<Bairro> bairros, string? busca)
    {
        if (string.IsNullOrWhiteSpace(busca))
        {
            return bairros;
        }

        string pattern = BuscaTextualNormalizada.CriarPadraoContem(busca);
        return bairros.Where(b =>
            EF.Functions.ILike(b.NomeNormalizado, pattern, BuscaTextualNormalizada.Escape));
    }
}
