namespace Unifesspa.Geo.Application.Queries.CidadeHierarquia;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Application.Mappings;

public static class ListarLogradourosQueryHandler
{
    public static async Task<ListarLogradourosResult> Handle(
        ListarLogradourosQuery query,
        ILogradouroReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(reader);

        (bool cidadeExiste, IReadOnlyList<LogradouroComBairro> itens, Guid? anteriorAfterId, Guid? proximoAfterId) =
            await reader
                .ListarPorCidadeAsync(
                    query.CodigoIbge, query.AfterId, query.Limit, query.Direction, query.Busca, cancellationToken)
                .ConfigureAwait(false);

        LogradouroResumoDto[] items = [.. itens.Select(l => l.ToDto(query.CodigoIbge))];
        return new ListarLogradourosResult(cidadeExiste, items, anteriorAfterId, proximoAfterId);
    }
}
