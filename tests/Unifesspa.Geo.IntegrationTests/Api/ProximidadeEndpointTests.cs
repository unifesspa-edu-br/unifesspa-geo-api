namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Consulta de proximidade geoespacial contra a API real e o PostGIS. Leitura
/// autenticada sem papel administrativo. Ponto de referência fixo próximo a Marabá
/// (<c>lat=-5.35</c>, <c>long=-49.13</c>). A collection é serial; cada teste apaga e
/// semeia cidades/logradouros com coordenada (<c>geography(Point,4326)</c>).
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ProximidadeEndpointTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDeProximidade _cenario;

    public ProximidadeEndpointTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDeProximidade(fixture);
    }

    [Fact(DisplayName = "CA-01: cidades dentro do raio, ordenadas por distância, com DistanciaKm e _links")]
    public async Task CidadesProximas_OrdenaPorDistancia()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, "/api/cidades/proximas?lat=-5.35&long=-49.13&raioKm=100");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement raiz = await LerRaizAsync(resposta);
        raiz.ValueKind.Should().Be(JsonValueKind.Array);
        List<string> nomes = LerNomes(raiz);
        nomes.Should().Equal("Marabá", "Eldorado dos Carajás");
        nomes.Should().NotContain("São Paulo");                  // > 100 km
        nomes.Should().NotContain("Bom Jesus do Tocantins");     // sem coordenada
        ConferirDistanciaELinkDaCidadeMaisProxima(raiz);
    }

    [Fact(DisplayName = "CA-03: logradouros dentro do raio, ordenados por distância, com cidade e _links.cep")]
    public async Task LogradourosProximos_PorPonto()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, "/api/logradouros/proximos?lat=-5.35&long=-49.13&raioKm=50");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement raiz = await LerRaizAsync(resposta);
        List<string> nomes = LerNomes(raiz);
        nomes.Should().Equal("A", "B");          // Rua A (~1 km) antes de Avenida B (~9 km)
        nomes.Should().NotContain("da Sé");      // São Paulo, longe
        ConferirCidadeELinksDoLogradouroMaisProximo(raiz[0]);
    }

    [Theory(DisplayName = "CA-04: parâmetro ausente ou fora de faixa retorna 400")]
    [InlineData("/api/cidades/proximas?long=-49.13&raioKm=100")]            // sem lat
    [InlineData("/api/cidades/proximas?lat=-5.35&raioKm=100")]              // sem long
    [InlineData("/api/cidades/proximas?lat=-5.35&long=-49.13")]            // sem raioKm
    [InlineData("/api/cidades/proximas?lat=200&long=-49.13&raioKm=10")]    // lat fora de faixa
    [InlineData("/api/cidades/proximas?lat=-5.35&long=-200&raioKm=10")]    // long fora de faixa
    [InlineData("/api/cidades/proximas?lat=-5.35&long=-49.13&raioKm=0")]   // raio = 0
    [InlineData("/api/cidades/proximas?lat=-5.35&long=-49.13&raioKm=9999")] // raio > teto
    [InlineData("/api/cidades/proximas?lat=-5.35&long=-49.13&raioKm=10&limit=0")] // limit <= 0
    public async Task CidadesProximas_Validacao_400(string rota)
    {
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, rota);
        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "CA-05: registro sem coordenada (ou sem lat/long) não aparece no resultado")]
    public async Task CidadesProximas_SemCoordenada_Exclui()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, "/api/cidades/proximas?lat=-5.35&long=-49.13&raioKm=100");
        List<string> nomes = LerNomes(await LerRaizAsync(resposta));
        nomes.Should().NotContain("Bom Jesus do Tocantins", "Coordenada null não entra (ST_DWithin sobre NULL é falso)");
        nomes.Should().NotContain("Brejo Grande do Araguaia", "Coordenada presente mas lat/long nulos também é excluída");
    }

    [Fact(DisplayName = "CA-06: ponto no oceano com raio pequeno retorna 200 com lista vazia")]
    public async Task CidadesProximas_RaioVazio_ListaVazia()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, "/api/cidades/proximas?lat=0&long=-30&raioKm=10");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        (await LerRaizAsync(resposta)).GetArrayLength().Should().Be(0);
    }

    [Fact(DisplayName = "CA-06: limit=1 devolve apenas a cidade mais próxima (top-N por distância)")]
    public async Task CidadesProximas_Limit_RespeitaTopN()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, "/api/cidades/proximas?lat=-5.35&long=-49.13&raioKm=200&limit=1");
        JsonElement raiz = await LerRaizAsync(resposta);
        raiz.GetArrayLength().Should().Be(1);
        raiz[0].GetProperty("nome").GetString().Should().Be("Marabá");
    }

    [Fact(DisplayName = "CA-07: Accept vendor incompatível retorna 406; cidade-proxima.v1 é aceito")]
    public async Task CidadesProximas_VendorMime_406()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resp406 = await EnviarAceitandoAsync(client, "application/vnd.uniplus.cidade-proxima.v2+json");
        resp406.StatusCode.Should().Be(HttpStatusCode.NotAcceptable);

        using HttpResponseMessage resp200 = await EnviarAceitandoAsync(client, "application/vnd.uniplus.cidade-proxima.v1+json");
        resp200.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "CA-08: ponto é Point(long,lat); inverter lat/long muda o resultado; precisão decimal(9,6)")]
    public async Task CidadesProximas_OrdemCoordenadas_LongLat()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        // Correto: (lat=-5.35, long=-49.13) próximo a Marabá; latitude e longitude espelham o
        // seed em decimal(9,6).
        JsonElement correto = await LerCidadesProximasAsync(client, "lat=-5.35&long=-49.13&raioKm=50");
        JsonElement maraba = correto.EnumerateArray().First(c => c.GetProperty("nome").GetString() == "Marabá");
        maraba.GetProperty("latitude").GetDecimal().Should().Be(-5.36867m);
        maraba.GetProperty("longitude").GetDecimal().Should().Be(-49.11731m);

        // Invertido (lat=-49.13, long=-5.35): ambos válidos, mas caem no Atlântico Sul,
        // longe de Marabá → a cidade não aparece. Prova a convenção X=long, Y=lat.
        JsonElement invertido = await LerCidadesProximasAsync(client, "lat=-49.13&long=-5.35&raioKm=50");
        LerNomes(invertido).Should().NotContain("Marabá");
    }

    [Fact(DisplayName = "Contrato: lat/long/raioKm são required no OpenAPI; limit é opcional")]
    public async Task Contrato_ParametrosObrigatorios_MarcadosRequired()
    {
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage spec = await GeoReferenceSeed.Obter(client, "/openapi/geo.json");
        spec.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = JsonDocument.Parse(await spec.Content.ReadAsStringAsync());
        Dictionary<string, bool> required = ObrigatoriedadeDosParametros(doc.RootElement);
        required["lat"].Should().BeTrue();
        required["long"].Should().BeTrue();
        required["raioKm"].Should().BeTrue();
        required["limit"].Should().BeFalse("limit tem default — permanece opcional");
    }

    private static Dictionary<string, bool> ObrigatoriedadeDosParametros(JsonElement especificacao)
    {
        JsonElement parametros = especificacao
            .GetProperty("paths")
            .GetProperty("/api/cidades/proximas")
            .GetProperty("get")
            .GetProperty("parameters");
        Dictionary<string, bool> required = [];
        foreach (JsonElement p in parametros.EnumerateArray())
        {
            string nome = p.GetProperty("name").GetString()!;
            required[nome] = p.TryGetProperty("required", out JsonElement r) && r.GetBoolean();
        }

        return required;
    }

    private static void ConferirDistanciaELinkDaCidadeMaisProxima(JsonElement raiz)
    {
        JsonElement primeiro = raiz[0];
        primeiro.GetProperty("distanciaKm").GetDouble().Should().BeGreaterThan(0);
        primeiro.GetProperty("_links").GetProperty("cidade").GetString().Should().Be("/api/cidades/1500402");

        double d0 = raiz[0].GetProperty("distanciaKm").GetDouble();
        double d1 = raiz[1].GetProperty("distanciaKm").GetDouble();
        d0.Should().BeLessThan(d1, "a lista é ordenada por distância crescente");
    }

    private static void ConferirCidadeELinksDoLogradouroMaisProximo(JsonElement primeiro)
    {
        primeiro.GetProperty("cidadeNome").GetString().Should().Be("Marabá");
        primeiro.GetProperty("cidadeCodigoIbge").GetString().Should().Be("1500402");
        primeiro.GetProperty("distanciaKm").GetDouble().Should().BeGreaterThan(0);

        JsonElement links = primeiro.GetProperty("_links");
        links.GetProperty("cidade").GetString().Should().Be("/api/cidades/1500402");
        links.GetProperty("cep").GetString().Should().Be("/api/cep/68500000");
    }

    private static async Task<HttpResponseMessage> EnviarAceitandoAsync(HttpClient client, string vendorMime)
    {
        using HttpRequestMessage requisicao = new(
            HttpMethod.Get, "/api/cidades/proximas?lat=-5.35&long=-49.13&raioKm=100");
        requisicao.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(vendorMime));
        return await client.SendAsync(requisicao);
    }

    private static async Task<JsonElement> LerCidadesProximasAsync(HttpClient client, string consulta)
    {
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, $"/api/cidades/proximas?{consulta}");
        return await LerRaizAsync(resposta);
    }

    private static async Task<JsonElement> LerRaizAsync(HttpResponseMessage resposta)
    {
        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static List<string> LerNomes(JsonElement array) =>
        [.. array.EnumerateArray().Select(item => item.GetProperty("nome").GetString()!)];
}
