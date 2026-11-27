namespace Unifesspa.Geo.IntegrationTests.Api;

using Unifesspa.Geo.Application.Abstractions.Messaging;
using Unifesspa.Geo.Application.Queries.CidadeHierarquia;

/// <summary>Despachante de consultas em que nada existe: respostas únicas e vazias, sem banco.</summary>
internal sealed class ConsultasSemResultadoParaTeste : IQueryBus
{
    public Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken ct = default)
    {
        object? resultado = query switch
        {
            ListarDistritosQuery => new ListarDistritosResult(false, [], null, null),
            ListarBairrosQuery => new ListarBairrosResult(false, [], null, null),
            ListarLogradourosQuery => new ListarLogradourosResult(false, [], null, null),
            _ => null,
        };

        return Task.FromResult((TResponse)resultado!);
    }
}
