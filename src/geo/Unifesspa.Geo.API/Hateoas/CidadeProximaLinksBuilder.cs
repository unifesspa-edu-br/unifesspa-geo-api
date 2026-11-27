namespace Unifesspa.Geo.API.Hateoas;

using System.Diagnostics.CodeAnalysis;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;

/// <summary>
/// Builder de <c>_links</c> hypermedia (HATEOAS Level 1, ADR-0029) para
/// <see cref="CidadeProximaDto"/> (item de ranking por distância). Relação
/// <c>cidade</c> (<c>GET /api/cidades/{codigoIbge}</c>) — leva ao detalhe completo da
/// cidade. Sem <c>self</c>: o item de proximidade não é um recurso canônico próprio.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via AddSingleton<IResourceLinksBuilder<CidadeProximaDto>, CidadeProximaLinksBuilder>().")]
internal sealed class CidadeProximaLinksBuilder : IResourceLinksBuilder<CidadeProximaDto>
{
    private readonly CaminhosDosRecursosDoGeo _caminhos;

    public CidadeProximaLinksBuilder(CaminhosDosRecursosDoGeo caminhos)
    {
        ArgumentNullException.ThrowIfNull(caminhos);
        _caminhos = caminhos;
    }

    public IReadOnlyDictionary<string, string> Build(CidadeProximaDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cidade"] = _caminhos.DaCidade(dto.CodigoIbge),
        };
    }
}
