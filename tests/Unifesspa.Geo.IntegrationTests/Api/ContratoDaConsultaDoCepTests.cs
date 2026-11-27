namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Contrato HTTP da consulta de CEP, contra a API real e o PostGIS: máscara, CEP malformado,
/// CEP sem cobertura e negociação do tipo de mídia. A collection é serial; cada teste apaga
/// e semeia os dados.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ContratoDaConsultaDoCepTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDaConsultaDoCep _cenario;

    public ContratoDaConsultaDoCepTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDaConsultaDoCep(fixture);
    }

    [Fact(DisplayName = "CA-05: CEP com máscara é normalizado e resolvido")]
    public async Task Cep_ComMascara_Resolve()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        JsonElement raiz = await RespostaDoCep.ResolverAsync(client, "/api/cep/01001-000");

        raiz.GetProperty("logradouro").GetString().Should().Be("Praça da Sé");
    }

    [Theory(DisplayName = "CA-05: CEP malformado (≠8 dígitos, não-numérico) retorna 400")]
    [InlineData("/api/cep/123")]
    [InlineData("/api/cep/010010000")]
    [InlineData("/api/cep/0100100a")]
    [InlineData("/api/cep/abcdefgh")]
    public async Task Cep_Malformado_400(string rota)
    {
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, rota);
        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "CA-06: CEP bem-formado sem cobertura retorna 404")]
    public async Task Cep_Inexistente_404()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, "/api/cep/99999999");
        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "CA-09: Accept vendor incompatível retorna 406; vendor cep.v1 é aceito")]
    public async Task Cep_VendorMime_406()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpRequestMessage incompativel = new(HttpMethod.Get, "/api/cep/01001000");
        incompativel.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.uniplus.cep.v2+json"));
        using HttpResponseMessage resp406 = await client.SendAsync(incompativel);
        resp406.StatusCode.Should().Be(HttpStatusCode.NotAcceptable);

        using HttpRequestMessage compativel = new(HttpMethod.Get, "/api/cep/01001000");
        compativel.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.uniplus.cep.v1+json"));
        using HttpResponseMessage resp200 = await client.SendAsync(compativel);
        resp200.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
