namespace Unifesspa.Geo.Application.Queries.CidadeHierarquia;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Application.Mappings;
using Unifesspa.Geo.Domain.Entities;

public static class ListarBairrosQueryHandler
{
    public static async Task<ListarBairrosResult> Handle(
        ListarBairrosQuery query,
        IBairroReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(reader);

        (bool cidadeExiste, IReadOnlyList<Bairro> itens, Guid? anteriorAfterId, Guid? proximoAfterId) =
            await reader
                .ListarPorCidadeAsync(
                    query.CodigoIbge, query.AfterId, query.Limit, query.Direction, query.Busca, cancellationToken)
                .ConfigureAwait(false);

        BairroDto[] items = [.. itens.Select(b => b.ToDto(query.CodigoIbge))];
        return new ListarBairrosResult(cidadeExiste, items, anteriorAfterId, proximoAfterId);
    }
}
