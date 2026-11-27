namespace Unifesspa.Geo.API.Hateoas;

using System.Diagnostics.CodeAnalysis;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;

/// <summary>
/// Builder de <c>_links</c> hypermedia (HATEOAS Level 1, ADR-0029) para
/// <see cref="LogradouroProximoDto"/> (item de ranking por distância). Relações
/// <c>cidade</c> (<c>GET /api/cidades/{codigoIbge}</c>) e <c>cep</c>
/// (<c>GET /api/cep/{cep}</c>) — leva ao lookup completo do endereço. Sem <c>self</c>:
/// a API não expõe detalhe de logradouro por Id.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via AddSingleton<IResourceLinksBuilder<LogradouroProximoDto>, LogradouroProximoLinksBuilder>().")]
internal sealed class LogradouroProximoLinksBuilder : IResourceLinksBuilder<LogradouroProximoDto>
{
    private readonly CaminhosDosRecursosDoGeo _caminhos;

    public LogradouroProximoLinksBuilder(CaminhosDosRecursosDoGeo caminhos)
    {
        ArgumentNullException.ThrowIfNull(caminhos);
        _caminhos = caminhos;
    }

    public IReadOnlyDictionary<string, string> Build(LogradouroProximoDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cidade"] = _caminhos.DaCidade(dto.CidadeCodigoIbge),
            ["cep"] = _caminhos.DaConsultaDoCep(dto.Cep),
        };
    }
}
