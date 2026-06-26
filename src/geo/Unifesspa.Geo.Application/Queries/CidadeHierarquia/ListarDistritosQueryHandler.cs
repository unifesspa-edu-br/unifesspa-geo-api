namespace Unifesspa.Geo.Application.Queries.CidadeHierarquia;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Application.Mappings;
using Unifesspa.Geo.Domain.Entities;

public static class ListarDistritosQueryHandler
{
    public static async Task<ListarDistritosResult> Handle(
        ListarDistritosQuery query,
        IDistritoReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(reader);

        (bool cidadeExiste, IReadOnlyList<Distrito> itens, Guid? anteriorAfterId, Guid? proximoAfterId) =
            await reader
                .ListarPorCidadeAsync(query.CodigoIbge, query.AfterId, query.Limit, query.Direction, cancellationToken)
                .ConfigureAwait(false);

        if (!cidadeExiste)
        {
            return new ListarDistritosResult(false, [], anteriorAfterId, proximoAfterId);
        }

        DistritoDto[] items = [.. itens.Select(d => d.ToDto(query.CodigoIbge))];
        return new ListarDistritosResult(true, items, anteriorAfterId, proximoAfterId);
    }
}
