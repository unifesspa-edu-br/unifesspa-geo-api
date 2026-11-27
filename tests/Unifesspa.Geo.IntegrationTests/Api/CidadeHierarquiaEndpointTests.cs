namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Endpoints autenticados de hierarquia e autocomplete Geo contra API real +
/// PostGIS, sem role administrativa. Cobre Cidade → Distrito/Bairro/Logradouro,
/// cursor, HATEOAS e boundary.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CidadeHierarquiaEndpointTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDaHierarquiaDeCidades _cenario;

    public CidadeHierarquiaEndpointTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDaHierarquiaDeCidades(fixture);
    }

    [Fact(DisplayName = "CA-01: distritos de uma cidade são paginados e têm _links cidade+collection sem self")]
    public async Task Distritos_PorCidade_Paginado()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();
        using HttpResponseMessage pagina1 = await GeoReferenceSeed.Obter(client, "/api/cidades/3550308/distritos?limit=1");
        pagina1.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement itensPagina1 = await LerArrayAsync(pagina1);
        itensPagina1.GetArrayLength().Should().Be(1);
        ConferirDistritoComLinksEPosicao(itensPagina1.EnumerateArray().Single());
        string? proximo = GeoReferenceSeed.ExtrairLink(pagina1, "next");
        proximo.Should().NotBeNull();
        proximo.Should().Contain("/api/cidades/3550308/distritos");
        using HttpResponseMessage pagina2 = await GeoReferenceSeed.Obter(client, proximo!);
        pagina2.StatusCode.Should().Be(HttpStatusCode.OK);
        List<string> nomes = [.. LerNomes(itensPagina1), .. LerNomes(await LerArrayAsync(pagina2))];
        nomes.Should().BeEquivalentTo(["Sé", "Pinheiros"]);
    }

    [Theory(DisplayName = "CA-02: busca de bairro é acento e caixa-insensível")]
    [InlineData("sa")]
    [InlineData("SÁ")]
    public async Task Bairros_Busca_AcentoInsensivel(string termo)
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, $"/api/cidades/3550308/bairros?q={Uri.EscapeDataString(termo)}&limit=100");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement itens = await LerArrayAsync(resposta);
        List<string> nomes = LerNomes(itens);
        nomes.Should().BeEquivalentTo(["Santa Cecília", "Saúde"]);
        nomes.Should().NotContain("Sé");
        ConferirLinksDeBairros(itens.EnumerateArray().First().GetProperty("_links"));
        itens.EnumerateArray()
            .Single(b => string.Equals(b.GetProperty("nome").GetString(), "Saúde", StringComparison.Ordinal))
            .GetProperty("latitude").GetDecimal().Should().Be(-23.61811m);
    }

    [Fact(DisplayName = "CA-03: autocomplete de logradouro busca por nome (acento-insensível), escopado à cidade, com _links.cep")]
    public async Task Logradouros_Autocomplete_PorCidade()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        // A busca corre sobre o nome normalizado (texto completo sem acento), então casa por
        // qualquer parte do logradouro, sem diferenciar acento nem caixa: "sé" → "Praça da Sé".
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, "/api/cidades/3550308/logradouros?q=s%C3%A9&limit=100");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement itens = await LerArrayAsync(resposta);
        itens.GetArrayLength().Should().Be(1);
        JsonElement item = itens.EnumerateArray().Single();
        ConferirPracaDaSe(item);
        ConferirLinksDaPracaDaSe(item.GetProperty("_links"));
    }

    [Fact(DisplayName = "CA-03 (#707): busca casa o TIPO do logradouro ('praça' → 'Praça da Sé'), escopada à cidade")]
    public async Task Logradouros_Autocomplete_PorTipo()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        // "praça" vive na coluna tipo, mas compõe o texto completo do nome normalizado: a busca
        // por tipo casa e fica escopada a São Paulo (Marabá tem "Praça São Félix", que não vaza).
        using HttpResponseMessage porTipo = await GeoReferenceSeed.Obter(
            client, "/api/cidades/3550308/logradouros?q=pra%C3%A7a&limit=100");
        porTipo.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement itens = await LerArrayAsync(porTipo);
        itens.GetArrayLength().Should().Be(1);
        itens.EnumerateArray().Single().GetProperty("nomeCompleto").GetString().Should().Be("Praça da Sé");
    }

    [Fact(DisplayName = "CA-03 (#707): busca por texto completo 'praça da sé' (tipo + nome) encontra 'Praça da Sé'")]
    public async Task Logradouros_Autocomplete_PorTextoCompleto()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, $"/api/cidades/3550308/logradouros?q={Uri.EscapeDataString("praça da sé")}&limit=100");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement itens = await LerArrayAsync(resposta);
        itens.GetArrayLength().Should().Be(1);
        itens.EnumerateArray().Single().GetProperty("nomeCompleto").GetString().Should().Be("Praça da Sé");
    }

    [Fact(DisplayName = "CA-03 (#707): resultados ordenados por relevância (similaridade), não por Id")]
    public async Task Logradouros_Autocomplete_OrdenadoPorRelevancia()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        // "Rua das Flores" e "Rua das Flores e Jardins" casam ambos "rua das flores"; o de
        // texto exato (similaridade máxima) precede o mais longo, independentemente do Id.
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, $"/api/cidades/3550308/logradouros?q={Uri.EscapeDataString("rua das flores")}&limit=100");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement itens = await LerArrayAsync(resposta);
        List<string> nomesCompletos =
            [.. itens.EnumerateArray().Select(i => i.GetProperty("nomeCompleto").GetString()!)];
        nomesCompletos.Should().Equal("Rua das Flores", "Rua das Flores e Jardins");
    }

    [Theory(DisplayName = "CA-08 (#709): autocomplete tolera abreviação de tipo ('av') e ordem das palavras")]
    [InlineData("av paulista")]        // abreviação de tipo (prefixo "av" → "avenida")
    [InlineData("paulista avenida")]   // palavras fora de ordem
    public async Task Logradouros_Autocomplete_AbreviacaoEOrdem(string termo)
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        // A similaridade de palavras casa o termo contra o texto completo independentemente de
        // abreviação ou ordem; o ILIKE rejeitaria ambos. O resultado esperado fica no topo do
        // ranking (similaridade de palavras e depois similaridade, ambas decrescentes).
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, $"/api/cidades/3550308/logradouros?q={Uri.EscapeDataString(termo)}&limit=100");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement itens = await LerArrayAsync(resposta);
        itens.EnumerateArray().First().GetProperty("nomeCompleto").GetString().Should().Be("Avenida Paulista");
    }

    [Fact(DisplayName = "CA-08 (#709): autocomplete tolera abreviação que não é prefixo ('pca' → 'Praça')")]
    public async Task Logradouros_Autocomplete_AbreviacaoNaoPrefixo()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        // "pca" não é prefixo de "praça": só a similaridade de palavras (trigramas) casa; a busca
        // por prefixo e o ILIKE falhariam.
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, $"/api/cidades/3550308/logradouros?q={Uri.EscapeDataString("pca da se")}&limit=100");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement itens = await LerArrayAsync(resposta);
        itens.EnumerateArray().First().GetProperty("nomeCompleto").GetString().Should().Be("Praça da Sé");
    }

    [Fact(DisplayName = "CA-08 (#709): autocomplete tolera pequeno erro de digitação (recall)")]
    public async Task Logradouros_Autocomplete_Typo()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        // O erro de digitação ("paulysta") ainda recupera o logradouro. A asserção é de
        // recuperação (Contain), não de ranking: em bases grandes ele pode ficar fora do topo.
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, $"/api/cidades/3550308/logradouros?q={Uri.EscapeDataString("avenida paulysta")}&limit=100");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement itens = await LerArrayAsync(resposta);
        itens.EnumerateArray()
            .Select(i => i.GetProperty("nomeCompleto").GetString())
            .Should().Contain("Avenida Paulista");
    }

    [Fact(DisplayName = "CA-04: cidade inexistente retorna 404; cidade existente sem filhos do filtro retorna 200 vazio")]
    public async Task Hierarquia_CidadeInexistente_404_EFiltroVazio_200()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage inexistente = await GeoReferenceSeed.Obter(client, "/api/cidades/9999999/distritos");
        inexistente.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using HttpResponseMessage vazio = await GeoReferenceSeed.Obter(client, "/api/cidades/3550308/bairros?q=zzzzz");
        vazio.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LerArrayAsync(vazio)).GetArrayLength().Should().Be(0);
    }

    [Theory(DisplayName = "CA-05: código IBGE malformado retorna 400")]
    [InlineData("/api/cidades/abc/distritos")]
    [InlineData("/api/cidades/123/bairros")]
    [InlineData("/api/cidades/35503080/logradouros")]
    public async Task Hierarquia_CodigoIbgeInvalido_400(string rota)
    {
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, rota);
        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "CA-06: cursor preserva escopo da cidade e q no header Link")]
    public async Task Hierarquia_Cursor_PreservaEscopoEFiltro()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();
        using HttpResponseMessage pagina1 = await GeoReferenceSeed.Obter(client, "/api/cidades/3550308/bairros?q=sa&limit=1");
        pagina1.StatusCode.Should().Be(HttpStatusCode.OK);
        string? proximo = GeoReferenceSeed.ExtrairLink(pagina1, "next");
        proximo.Should().NotBeNull();
        proximo.Should().Contain("/api/cidades/3550308/bairros").And.Contain("q=sa");
        List<string> primeiraPagina = LerNomes(await LerArrayAsync(pagina1));
        using HttpResponseMessage pagina2 = await GeoReferenceSeed.Obter(client, proximo!);
        pagina2.StatusCode.Should().Be(HttpStatusCode.OK);
        await ConferirVoltaParaAPrimeiraPaginaAsync(client, pagina2, primeiraPagina);
        string? seguinte = GeoReferenceSeed.ExtrairLink(pagina2, "next");
        List<string> nomes = [.. primeiraPagina, .. LerNomes(await LerArrayAsync(pagina2)), .. await ListaDaApi.NomesDasPaginasAsync(client, seguinte)];
        nomes.Should().BeEquivalentTo(["Santa Cecília", "Saúde"]);
    }

    [Fact(DisplayName = "CA-07: Accept vendor incompatível retorna 406; termo q longo retorna 400")]
    public async Task Hierarquia_VendorMime_TermoLongo()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resp406 = await EnviarAceitandoAsync(client, "application/vnd.uniplus.logradouro.v2+json");
        resp406.StatusCode.Should().Be(HttpStatusCode.NotAcceptable);
        using HttpResponseMessage resp200 = await EnviarAceitandoAsync(client, "application/vnd.uniplus.logradouro.v1+json");
        resp200.StatusCode.Should().Be(HttpStatusCode.OK);
        string termoLongo = new('a', 257);
        using HttpResponseMessage longo = await GeoReferenceSeed.Obter(
            client, $"/api/cidades/3550308/logradouros?q={termoLongo}");
        longo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static void ConferirDistritoComLinksEPosicao(JsonElement distrito)
    {
        distrito.GetProperty("cidadeCodigoIbge").GetString().Should().Be("3550308");
        JsonElement links = distrito.GetProperty("_links");
        links.GetProperty("cidade").GetString().Should().Be("/api/cidades/3550308");
        links.GetProperty("collection").GetString().Should().Be("/api/cidades/3550308/distritos");
        links.TryGetProperty("self", out _).Should().BeFalse();
        distrito.GetProperty("latitude").ValueKind.Should().Be(JsonValueKind.Number);
        distrito.GetProperty("longitude").ValueKind.Should().Be(JsonValueKind.Number);
    }

    private static void ConferirLinksDeBairros(JsonElement links)
    {
        links.GetProperty("cidade").GetString().Should().Be("/api/cidades/3550308");
        links.GetProperty("collection").GetString().Should().Be("/api/cidades/3550308/bairros");
        links.TryGetProperty("self", out _).Should().BeFalse();
    }

    private static void ConferirPracaDaSe(JsonElement item)
    {
        item.GetProperty("cep").GetString().Should().Be("01001000");
        item.GetProperty("tipo").GetString().Should().Be("Praça");
        item.GetProperty("nome").GetString().Should().Be("Sé");
        item.GetProperty("nomeCompleto").GetString().Should().Be("Praça da Sé");
        item.GetProperty("bairro").GetString().Should().Be("Sé");
        item.GetProperty("cidadeCodigoIbge").GetString().Should().Be("3550308");
        item.GetProperty("uf").GetString().Should().Be("SP");
    }

    private static void ConferirLinksDaPracaDaSe(JsonElement links)
    {
        links.GetProperty("cidade").GetString().Should().Be("/api/cidades/3550308");
        links.GetProperty("cep").GetString().Should().Be("/api/cep/01001000");
        links.GetProperty("collection").GetString().Should().Be("/api/cidades/3550308/logradouros");
        links.TryGetProperty("self", out _).Should().BeFalse();
    }

    // A página anterior de pagina2 aponta para o mesmo escopo e traz de volta a primeira página.
    private static async Task ConferirVoltaParaAPrimeiraPaginaAsync(
        HttpClient client, HttpResponseMessage pagina2, List<string> primeiraPagina)
    {
        string? anterior = GeoReferenceSeed.ExtrairLink(pagina2, "prev");
        anterior.Should().NotBeNull();
        anterior.Should().Contain("/api/cidades/3550308/bairros").And.Contain("q=sa");

        using HttpResponseMessage volta = await GeoReferenceSeed.Obter(client, anterior!);
        volta.StatusCode.Should().Be(HttpStatusCode.OK);
        LerNomes(await LerArrayAsync(volta)).Should().Equal(primeiraPagina);
    }

    private static async Task<HttpResponseMessage> EnviarAceitandoAsync(HttpClient client, string vendorMime)
    {
        using HttpRequestMessage requisicao = new(HttpMethod.Get, "/api/cidades/3550308/logradouros?q=praca");
        requisicao.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(vendorMime));
        return await client.SendAsync(requisicao);
    }

    private static async Task<JsonElement> LerArrayAsync(HttpResponseMessage resposta)
    {
        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static List<string> LerNomes(JsonElement array) =>
        [.. array.EnumerateArray().Select(item => item.GetProperty("nome").GetString()!)];
}
