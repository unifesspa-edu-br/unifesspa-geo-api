namespace Unifesspa.Geo.API.Hateoas;

using System.Diagnostics.CodeAnalysis;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;

/// <summary>
/// Builder de <c>_links</c> hypermedia (HATEOAS Level 1, ADR-0029) para
/// <see cref="CepResolvidoDto"/>. Relações: <c>cidade</c>
/// (<c>GET /api/cidades/{codigoIbge}</c>) e <c>estado</c>
/// (<c>GET /api/estados/{uf}</c>) — permitem navegar do CEP para a ficha completa
/// da cidade e do estado resolvidos.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via AddSingleton<IResourceLinksBuilder<CepResolvidoDto>, CepResolvidoLinksBuilder>().")]
internal sealed class CepResolvidoLinksBuilder : IResourceLinksBuilder<CepResolvidoDto>
{
    private readonly CaminhosDosRecursosDoGeo _caminhos;

    public CepResolvidoLinksBuilder(CaminhosDosRecursosDoGeo caminhos)
    {
        ArgumentNullException.ThrowIfNull(caminhos);
        _caminhos = caminhos;
    }

    public IReadOnlyDictionary<string, string> Build(CepResolvidoDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cidade"] = _caminhos.DaCidade(dto.CodigoIbge),
            ["estado"] = _caminhos.DoEstado(dto.Uf),
        };
    }
}
