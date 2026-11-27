namespace Unifesspa.Geo.API.Hateoas;

using System.Diagnostics.CodeAnalysis;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;

/// <summary>
/// Builder de <c>_links</c> para itens de autocomplete de logradouro. Não emite
/// <c>self</c>, pois a API ainda não expõe detalhe de logradouro por Id.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via AddSingleton<IResourceLinksBuilder<LogradouroResumoDto>, LogradouroResumoLinksBuilder>().")]
internal sealed class LogradouroResumoLinksBuilder : IResourceLinksBuilder<LogradouroResumoDto>
{
    private readonly CaminhosDosRecursosDoGeo _caminhos;

    public LogradouroResumoLinksBuilder(CaminhosDosRecursosDoGeo caminhos)
    {
        ArgumentNullException.ThrowIfNull(caminhos);
        _caminhos = caminhos;
    }

    public IReadOnlyDictionary<string, string> Build(LogradouroResumoDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cidade"] = _caminhos.DaCidade(dto.CidadeCodigoIbge),
            ["cep"] = _caminhos.DaConsultaDoCep(dto.Cep),
            ["collection"] = _caminhos.DosLogradourosDaCidade(dto.CidadeCodigoIbge),
        };
    }
}
