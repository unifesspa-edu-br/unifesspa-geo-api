namespace Unifesspa.Geo.IntegrationTests.Paginacao;

using Unifesspa.Geo.Application.Abstractions.Messaging;
using Unifesspa.Geo.Application.Queries.Cidades;
using Unifesspa.Geo.Application.Queries.Estados;
using Unifesspa.Geo.Infrastructure.Core.Pagination;

/// <summary>
/// Despachante de consultas sem banco: guarda a página que a fronteira HTTP montou a partir da
/// requisição e responde com uma lista vazia.
/// </summary>
internal sealed class ConsultaDeListagemCapturada : IQueryBus
{
    public PageRequest? PaginaRecebida { get; private set; }

    public Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken ct = default)
    {
        (PageRequest Pagina, object Resultado) lido = query switch
        {
            ListarEstadosQuery q => (new PageRequest(q.AfterId, q.Limit, q.Direction, q.AfterSortKey), new ListarEstadosResult([], null, null)),
            ListarCidadesQuery q => (new PageRequest(q.AfterId, q.Limit, q.Direction, q.AfterSortKey), new ListarCidadesResult([], null, null)),
            _ => throw new NotSupportedException($"Consulta {query.GetType().Name} não prevista neste teste."),
        };

        PaginaRecebida = lido.Pagina;
        return Task.FromResult((TResponse)lido.Resultado);
    }
}
