namespace Unifesspa.Geo.API.Hateoas;

using System.Diagnostics.CodeAnalysis;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;

/// <summary>
/// Builder de <c>_links</c> hypermedia (HATEOAS Level 1, ADR-0029) para
/// <see cref="ImportacaoGeoDto"/>. Relação em V1: <c>self</c> (URI de acompanhamento da
/// execução). Action links não aparecem aqui (ADR-0029).
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via IServiceProvider.AddSingleton<IResourceLinksBuilder<ImportacaoGeoDto>, ImportacaoGeoLinksBuilder>().")]
internal sealed class ImportacaoGeoLinksBuilder : IResourceLinksBuilder<ImportacaoGeoDto>
{
    private readonly CaminhosDosRecursosDoGeo _caminhos;

    public ImportacaoGeoLinksBuilder(CaminhosDosRecursosDoGeo caminhos)
    {
        ArgumentNullException.ThrowIfNull(caminhos);
        _caminhos = caminhos;
    }

    public IReadOnlyDictionary<string, string> Build(ImportacaoGeoDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["self"] = _caminhos.DaImportacao(dto.Id),
        };
    }
}
