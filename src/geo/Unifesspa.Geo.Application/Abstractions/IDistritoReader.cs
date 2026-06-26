namespace Unifesspa.Geo.Application.Abstractions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// Leitor read-side de distritos vinculados a uma Cidade vigente.
/// </summary>
public interface IDistritoReader
{
    Task<(bool CidadeExiste, IReadOnlyList<Distrito> Itens, Guid? AnteriorAfterId, Guid? ProximoAfterId)> ListarPorCidadeAsync(
        string codigoIbge,
        Guid? afterId,
        int limit,
        PaginationDirection direction,
        CancellationToken cancellationToken);
}
