namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Endpoints de leitura autenticada de estados contra a API real e o PostGIS.
/// Leitura sem papel administrativo. Cobre CA-01, CA-07, CA-09 e CA-10.
/// A collection é serial; cada teste apaga e semeia.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class EstadosEndpointTests
{
    private static readonly string[] UfsSemeadas = ["AC", "AL", "AM", "BA", "CE"];

    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDeEstados _cenario;

    public EstadosEndpointTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDeEstados(fixture);
    }

    [Fact(DisplayName = "CA-01/CA-10: lista de estados pagina por cursor estável e cada item tem _links.self")]
    public async Task Estados_Lista_Paginada()
    {
        await _cenario.SemearEstadosAsync(UfsSemeadas);
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();
        using HttpResponseMessage pagina1 = await GeoReferenceSeed.Obter(client, "/api/estados?limit=2");
        pagina1.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement itens1 = await LerArrayAsync(pagina1);
        itens1.GetArrayLength().Should().Be(2);
        ConferirLinkSelfDeCadaEstado(itens1);
        string? proximo = GeoReferenceSeed.ExtrairLink(pagina1, "next");
        proximo.Should().NotBeNull("há mais páginas");
        (List<string> seguintes, bool viuPrev) = await ColetarUfsDasProximasPaginasAsync(client, proximo!);
        List<string> coletadas = [.. ExtrairUfs(itens1), .. seguintes];
        coletadas.Should().BeEquivalentTo(UfsSemeadas, "a paginação cobre todo o conjunto sem repetir/saltar");
        coletadas.Should().OnlyHaveUniqueItems();
        viuPrev.Should().BeTrue("páginas após a primeira emitem rel=prev");
    }

    [Fact(DisplayName = "#700: lista de estados vem ordenada alfabeticamente por nome, estável ao paginar")]
    public async Task Estados_OrdenadosAlfabeticamente()
    {
        // Semeia fora de ordem: a ordem alfabética dos nomes difere da ordem de inserção/Id.
        await _cenario.SemearForaDeOrdemAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        List<string> nomes = await ListaDaApi.NomesDasPaginasAsync(client, "/api/estados?limit=2");

        nomes.Should().Equal(
            "Acre", "Bahia", "Minas Gerais", "Pará", "Sergipe");
    }

    [Fact(DisplayName = "#700: navegação prev volta à página anterior na ordem alfabética (sem duplicata/salto)")]
    public async Task Estados_NavegacaoPrev_Alfabetica()
    {
        await _cenario.SemearForaDeOrdemAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage p1 = await GeoReferenceSeed.Obter(client, "/api/estados?limit=2");
        ExtrairNomes(await LerArrayAsync(p1)).Should().Equal("Acre", "Bahia");
        string proximo = GeoReferenceSeed.ExtrairLink(p1, "next")!;

        using HttpResponseMessage p2 = await GeoReferenceSeed.Obter(client, proximo);
        ExtrairNomes(await LerArrayAsync(p2)).Should().Equal("Minas Gerais", "Pará");
        string anterior = GeoReferenceSeed.ExtrairLink(p2, "prev")!;

        // Voltar (prev) a partir da página 2 reentrega exatamente a página 1, em ordem.
        using HttpResponseMessage volta = await GeoReferenceSeed.Obter(client, anterior);
        ExtrairNomes(await LerArrayAsync(volta)).Should().Equal("Acre", "Bahia");
    }

    [Fact(DisplayName = "CA-09: detalhe do estado por UF traz núcleo + região + capital + coordenadas + _links")]
    public async Task Estado_Detalhe_PorUf()
    {
        await _cenario.SemearParaAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, "/api/estados/PA");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        ConferirDadosDoPara(doc.RootElement);
        ConferirLinksDoPara(doc.RootElement.GetProperty("_links"));
    }

    [Theory(DisplayName = "CA-09: UF case-insensitive resolve o mesmo estado")]
    [InlineData("pa")]
    [InlineData("Pa")]
    public async Task Estado_Detalhe_PorUf_CaseInsensitive(string uf)
    {
        await _cenario.SemearParaAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, $"/api/estados/{uf}");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("uf").GetString().Should().Be("PA");
    }

    [Fact(DisplayName = "CA-09: UF bem-formada mas inexistente retorna 404")]
    public async Task Estado_Detalhe_Inexistente_404()
    {
        await _cenario.SemearParaAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, "/api/estados/ZZ");
        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory(DisplayName = "CA-09: UF malformada (≠2 letras) retorna 400")]
    [InlineData("/api/estados/X")]
    [InlineData("/api/estados/ABC")]
    [InlineData("/api/estados/12")]
    public async Task Estado_Detalhe_Malformada_400(string rota)
    {
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, rota);
        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "CA-07: Accept vendor incompatível retorna 406; vendor estado.v1 é aceito")]
    public async Task Estados_VendorMime()
    {
        await _cenario.SemearParaAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resp406 = await EnviarAceitandoAsync(client, "application/vnd.uniplus.estado.v2+json");
        resp406.StatusCode.Should().Be(HttpStatusCode.NotAcceptable);

        using HttpResponseMessage resp200 = await EnviarAceitandoAsync(client, "application/vnd.uniplus.estado.v1+json");
        resp200.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "ADR-0092: estado stale (vigente=false) não aparece na lista nem no detalhe")]
    public async Task Estados_NaoVigente_NaoVaza()
    {
        await _cenario.GravarEstadoVigenteEObsoletoAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage lista = await GeoReferenceSeed.Obter(client, "/api/estados?limit=100");
        JsonElement itens = await LerArrayAsync(lista);
        ExtrairUfs(itens).Should().Contain("PA").And.NotContain("ZZ");

        using HttpResponseMessage detalhe = await GeoReferenceSeed.Obter(client, "/api/estados/ZZ");
        detalhe.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void ConferirLinkSelfDeCadaEstado(JsonElement itens)
    {
        foreach (JsonElement item in itens.EnumerateArray())
        {
            string uf = item.GetProperty("uf").GetString()!;
            item.GetProperty("_links").GetProperty("self").GetString()
                .Should().Be($"/api/estados/{uf}");
        }
    }

    private static void ConferirDadosDoPara(JsonElement raiz)
    {
        raiz.GetProperty("uf").GetString().Should().Be("PA");
        raiz.GetProperty("nome").GetString().Should().Be("Pará");
        raiz.GetProperty("regiao").GetString().Should().Be("Norte");
        raiz.GetProperty("capital").GetString().Should().Be("Belém");
        raiz.GetProperty("latitude").GetDecimal().Should().Be(-3.79m);
    }

    private static void ConferirLinksDoPara(JsonElement links)
    {
        links.GetProperty("self").GetString().Should().Be("/api/estados/PA");
        links.GetProperty("cidades").GetString().Should().Be("/api/cidades?uf=PA");
        links.GetProperty("collection").GetString().Should().Be("/api/estados");
    }

    // Percorre as páginas seguindo o cursor 'next': a união com a primeira página é o conjunto
    // semeado, sem duplicatas (keyset estável — ADR-0089/0094). A ordem alfabética é coberta
    // pelos testes dedicados. Informa se alguma página seguinte trouxe o link prev.
    private static async Task<(List<string> Ufs, bool ViuPrev)> ColetarUfsDasProximasPaginasAsync(
        HttpClient client, string proximo)
    {
        List<string> ufs = [];
        bool viuPrev = false;
        string? rota = proximo;
        while (rota is not null)
        {
            using HttpResponseMessage pagina = await GeoReferenceSeed.Obter(client, rota);
            pagina.StatusCode.Should().Be(HttpStatusCode.OK);
            ufs.AddRange(ExtrairUfs(await LerArrayAsync(pagina)));
            viuPrev |= GeoReferenceSeed.ExtrairLink(pagina, "prev") is not null;
            rota = GeoReferenceSeed.ExtrairLink(pagina, "next");
        }

        return (ufs, viuPrev);
    }

    private static async Task<HttpResponseMessage> EnviarAceitandoAsync(HttpClient client, string vendorMime)
    {
        using HttpRequestMessage requisicao = new(HttpMethod.Get, "/api/estados");
        requisicao.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(vendorMime));
        return await client.SendAsync(requisicao);
    }

    private static async Task<JsonElement> LerArrayAsync(HttpResponseMessage resposta)
    {
        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static List<string> ExtrairUfs(JsonElement array) =>
        [.. array.EnumerateArray().Select(item => item.GetProperty("uf").GetString()!)];

    private static List<string> ExtrairNomes(JsonElement array) =>
        [.. array.EnumerateArray().Select(item => item.GetProperty("nome").GetString()!)];
}
