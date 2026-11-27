namespace Unifesspa.Geo.API.Hateoas;

using System.Diagnostics.CodeAnalysis;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;

/// <summary>
/// Builder de <c>_links</c> hypermedia (HATEOAS Level 1, ADR-0029) para
/// <see cref="CidadeResumoDto"/> (item de coleção). Relação <c>self</c>
/// (<c>GET /api/cidades/{codigoIbge}</c>) — leva ao detalhe completo da cidade.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via AddSingleton<IResourceLinksBuilder<CidadeResumoDto>, CidadeResumoLinksBuilder>().")]
internal sealed class CidadeResumoLinksBuilder : IResourceLinksBuilder<CidadeResumoDto>
{
    private readonly CaminhosDosRecursosDoGeo _caminhos;

    public CidadeResumoLinksBuilder(CaminhosDosRecursosDoGeo caminhos)
    {
        ArgumentNullException.ThrowIfNull(caminhos);
        _caminhos = caminhos;
    }

    public IReadOnlyDictionary<string, string> Build(CidadeResumoDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["self"] = _caminhos.DaCidade(dto.CodigoIbge),
        };
    }
}
