namespace Unifesspa.Geo.API.Hateoas;

using System.Diagnostics.CodeAnalysis;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;

/// <summary>
/// Builder de <c>_links</c> hypermedia (HATEOAS Level 1, ADR-0029) para
/// <see cref="CidadeDetalheDto"/>. Relações: <c>self</c>
/// (<c>GET /api/cidades/{codigoIbge}</c>), <c>estado</c>
/// (<c>GET /api/estados/{uf}</c>), <c>collection</c> (<c>GET /api/cidades</c>) e
/// coleções descendentes da cidade.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via AddSingleton<IResourceLinksBuilder<CidadeDetalheDto>, CidadeDetalheLinksBuilder>().")]
internal sealed class CidadeDetalheLinksBuilder : IResourceLinksBuilder<CidadeDetalheDto>
{
    private readonly CaminhosDosRecursosDoGeo _caminhos;

    public CidadeDetalheLinksBuilder(CaminhosDosRecursosDoGeo caminhos)
    {
        ArgumentNullException.ThrowIfNull(caminhos);
        _caminhos = caminhos;
    }

    public IReadOnlyDictionary<string, string> Build(CidadeDetalheDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["self"] = _caminhos.DaCidade(dto.CodigoIbge),
            ["estado"] = _caminhos.DoEstado(dto.Uf),
            ["collection"] = _caminhos.DaListaDeCidades(),
            ["distritos"] = _caminhos.DosDistritosDaCidade(dto.CodigoIbge),
            ["bairros"] = _caminhos.DosBairrosDaCidade(dto.CodigoIbge),
            ["logradouros"] = _caminhos.DosLogradourosDaCidade(dto.CodigoIbge),
        };
    }
}
