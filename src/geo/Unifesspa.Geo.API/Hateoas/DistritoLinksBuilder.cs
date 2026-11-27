namespace Unifesspa.Geo.API.Hateoas;

using System.Diagnostics.CodeAnalysis;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;

/// <summary>
/// Builder de <c>_links</c> para itens de distrito. Não emite <c>self</c>, pois a
/// API ainda não expõe detalhe de distrito por Id.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via AddSingleton<IResourceLinksBuilder<DistritoDto>, DistritoLinksBuilder>().")]
internal sealed class DistritoLinksBuilder : IResourceLinksBuilder<DistritoDto>
{
    private readonly CaminhosDosRecursosDoGeo _caminhos;

    public DistritoLinksBuilder(CaminhosDosRecursosDoGeo caminhos)
    {
        ArgumentNullException.ThrowIfNull(caminhos);
        _caminhos = caminhos;
    }

    public IReadOnlyDictionary<string, string> Build(DistritoDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cidade"] = _caminhos.DaCidade(dto.CidadeCodigoIbge),
            ["collection"] = _caminhos.DosDistritosDaCidade(dto.CidadeCodigoIbge),
        };
    }
}
