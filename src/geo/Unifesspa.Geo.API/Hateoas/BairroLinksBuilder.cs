namespace Unifesspa.Geo.API.Hateoas;

using System.Diagnostics.CodeAnalysis;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;

/// <summary>
/// Builder de <c>_links</c> para itens de bairro. Não emite <c>self</c>, pois a API
/// ainda não expõe detalhe de bairro por Id.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via AddSingleton<IResourceLinksBuilder<BairroDto>, BairroLinksBuilder>().")]
internal sealed class BairroLinksBuilder : IResourceLinksBuilder<BairroDto>
{
    private readonly CaminhosDosRecursosDoGeo _caminhos;

    public BairroLinksBuilder(CaminhosDosRecursosDoGeo caminhos)
    {
        ArgumentNullException.ThrowIfNull(caminhos);
        _caminhos = caminhos;
    }

    public IReadOnlyDictionary<string, string> Build(BairroDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cidade"] = _caminhos.DaCidade(dto.CidadeCodigoIbge),
            ["collection"] = _caminhos.DosBairrosDaCidade(dto.CidadeCodigoIbge),
        };
    }
}
