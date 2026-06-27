namespace Unifesspa.Geo.Infrastructure.Core.Hateoas;

/// <summary>
/// Constrói o conjunto de <c>_links</c> hypermedia (HATEOAS Level 1) para um
/// DTO de recurso single, conforme ADR-0029.
/// </summary>
/// <remarks>
/// <para>
/// Implementações vivem no boundary HTTP (camada API ou Infrastructure) e
/// usam <see cref="Microsoft.AspNetCore.Routing.LinkGenerator"/> via DI para
/// derivar URIs <strong>relativas</strong> (sem scheme/host) a partir de
/// route templates — payloads ficam transportáveis através de proxies/subpaths.
/// </para>
/// <para>
/// Vedação intencional (ADR-0029 §"Esta ADR não decide"): action links
/// (<c>publicar</c>, <c>cancelar</c>, etc.) <strong>não</strong> aparecem em
/// <c>_links</c> em V1 — operações de mutação são descobertas via OpenAPI
/// (ADR-0030). Adicioná-las exige nova ADR superseding.
/// </para>
/// <para>
/// Implementações são registradas como <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton"/>
/// — a função é estado-pura sobre o <see cref="LinkGenerator"/> singleton.
/// </para>
/// </remarks>
/// <typeparam name="TDto">Tipo do DTO de recurso single (ex.: <c>EditalDto</c>).</typeparam>
public interface IResourceLinksBuilder<in TDto>
    where TDto : class
{
    /// <summary>
    /// Constrói o dicionário <c>_links</c> para o recurso. Chave em
    /// <c>snake_case</c> ASCII; valor é URI relativa começando em <c>/</c>.
    /// <c>self</c> sempre presente (invariante ADR-0029).
    /// </summary>
    IReadOnlyDictionary<string, string> Build(TDto dto);
}
