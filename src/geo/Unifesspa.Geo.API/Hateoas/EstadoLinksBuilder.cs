namespace Unifesspa.Geo.API.Hateoas;

using System.Diagnostics.CodeAnalysis;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;

/// <summary>
/// Builder de <c>_links</c> hypermedia (HATEOAS Level 1, ADR-0029) para
/// <see cref="EstadoDto"/>. Relações: <c>self</c> (<c>GET /api/estados/{uf}</c>),
/// <c>cidades</c> (lista de cidades da UF, <c>GET /api/cidades?uf={uf}</c>) e
/// <c>collection</c> (<c>GET /api/estados</c>).
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via AddSingleton<IResourceLinksBuilder<EstadoDto>, EstadoLinksBuilder>().")]
internal sealed class EstadoLinksBuilder : IResourceLinksBuilder<EstadoDto>
{
    private readonly CaminhosDosRecursosDoGeo _caminhos;

    public EstadoLinksBuilder(CaminhosDosRecursosDoGeo caminhos)
    {
        ArgumentNullException.ThrowIfNull(caminhos);
        _caminhos = caminhos;
    }

    public IReadOnlyDictionary<string, string> Build(EstadoDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["self"] = _caminhos.DoEstado(dto.Uf),
            ["cidades"] = _caminhos.DaListaDeCidadesDaUf(dto.Uf),
            ["collection"] = _caminhos.DaListaDeEstados(),
        };
    }
}
