namespace Unifesspa.Geo.IntegrationTests.Api;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Infrastructure.Core.Hateoas;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Cada tipo de resposta devolve exatamente estes links, nesta ordem, com estes caminhos. A
/// montagem não precisa de banco: usa só o gerador de rotas da API.
/// </summary>
public sealed class LinksDeNavegacaoDosRecursosTests : IClassFixture<GeoOpenApiFactory>
{
    private readonly GeoOpenApiFactory _factory;

    public LinksDeNavegacaoDosRecursosTests(GeoOpenApiFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "Detalhe da cidade aponta para si, o estado, a lista e as coleções da cidade")]
    public void DetalheDaCidade_TemOsLinksNaOrdem() =>
        ConferirLinks(
            RecursosDeExemploParaLinks.CidadeDetalhe(),
            "self=/api/cidades/1500402",
            "estado=/api/estados/PA",
            "collection=/api/cidades",
            "distritos=/api/cidades/1500402/distritos",
            "bairros=/api/cidades/1500402/bairros",
            "logradouros=/api/cidades/1500402/logradouros");

    [Fact(DisplayName = "Estado aponta para si, as cidades da UF e a lista de estados")]
    public void Estado_TemOsLinksNaOrdem() =>
        ConferirLinks(
            RecursosDeExemploParaLinks.Estado(),
            "self=/api/estados/PA",
            "cidades=/api/cidades?uf=PA",
            "collection=/api/estados");

    [Fact(DisplayName = "CEP resolvido aponta para a cidade e o estado")]
    public void CepResolvido_TemOsLinksNaOrdem() =>
        ConferirLinks(
            RecursosDeExemploParaLinks.CepResolvido(),
            "cidade=/api/cidades/1500402",
            "estado=/api/estados/PA");

    [Fact(DisplayName = "Logradouro por proximidade aponta para a cidade e a consulta do CEP")]
    public void LogradouroProximo_TemOsLinksNaOrdem() =>
        ConferirLinks(
            RecursosDeExemploParaLinks.LogradouroProximo(),
            "cidade=/api/cidades/1500402",
            "cep=/api/cep/68500000");

    [Fact(DisplayName = "Logradouro da lista aponta para a cidade, a consulta do CEP e a lista de logradouros")]
    public void LogradouroResumo_TemOsLinksNaOrdem() =>
        ConferirLinks(
            RecursosDeExemploParaLinks.LogradouroResumo(),
            "cidade=/api/cidades/1500402",
            "cep=/api/cep/68500000",
            "collection=/api/cidades/1500402/logradouros");

    [Fact(DisplayName = "Distrito aponta para a cidade e a lista de distritos")]
    public void Distrito_TemOsLinksNaOrdem() =>
        ConferirLinks(
            RecursosDeExemploParaLinks.Distrito(),
            "cidade=/api/cidades/1500402",
            "collection=/api/cidades/1500402/distritos");

    [Fact(DisplayName = "Bairro aponta para a cidade e a lista de bairros")]
    public void Bairro_TemOsLinksNaOrdem() =>
        ConferirLinks(
            RecursosDeExemploParaLinks.Bairro(),
            "cidade=/api/cidades/1500402",
            "collection=/api/cidades/1500402/bairros");

    [Fact(DisplayName = "Cidade da lista aponta para si")]
    public void CidadeResumo_TemOsLinksNaOrdem() =>
        ConferirLinks(RecursosDeExemploParaLinks.CidadeResumo(), "self=/api/cidades/1500402");

    [Fact(DisplayName = "Cidade por proximidade aponta para a cidade")]
    public void CidadeProxima_TemOsLinksNaOrdem() =>
        ConferirLinks(RecursosDeExemploParaLinks.CidadeProxima(), "cidade=/api/cidades/1500402");

    [Fact(DisplayName = "Importação aponta para o próprio acompanhamento")]
    public void Importacao_TemOsLinksNaOrdem()
    {
        Guid id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        ConferirLinks(
            RecursosDeExemploParaLinks.Importacao(id),
            "self=/api/admin/geo/importacoes/11111111-2222-3333-4444-555555555555");
    }

    private void ConferirLinks<TDto>(TDto recurso, params string[] esperados)
        where TDto : class
    {
        IResourceLinksBuilder<TDto> montador = _factory.Services.GetRequiredService<IResourceLinksBuilder<TDto>>();

        montador.Build(recurso).Select(par => $"{par.Key}={par.Value}").Should().Equal(esperados);
    }
}
