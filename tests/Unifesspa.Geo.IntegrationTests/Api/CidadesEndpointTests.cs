namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Endpoints de leitura autenticada de cidades contra a API real e o PostGIS.
/// Leitura sem papel administrativo. Cobre CA-02 a CA-08. A collection é serial;
/// cada teste apaga e semeia.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CidadesEndpointTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDeCidades _cenario;

    public CidadesEndpointTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDeCidades(fixture);
    }

    [Fact(DisplayName = "CA-02: filtro por UF retorna só cidades da UF; UF inexistente é lista vazia (200)")]
    public async Task Cidades_FiltraPorUf()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage soPara = await GeoReferenceSeed.Obter(client, "/api/cidades?uf=PA&limit=100");
        soPara.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement itensPa = await LerArrayAsync(soPara);
        itensPa.EnumerateArray().Select(c => c.GetProperty("uf").GetString())
            .Should().OnlyContain(uf => uf == "PA").And.HaveCountGreaterThan(1);

        using HttpResponseMessage inexistente = await GeoReferenceSeed.Obter(client, "/api/cidades?uf=ZZ&limit=100");
        inexistente.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LerArrayAsync(inexistente)).GetArrayLength().Should().Be(0);
    }

    [Theory(DisplayName = "CA-03: busca por nome é acento e caixa-insensível")]
    [InlineData("maraba")]
    [InlineData("MARABÁ")]
    [InlineData("marabá")]
    public async Task Cidades_Busca_AcentoInsensivel(string termo)
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, $"/api/cidades?q={Uri.EscapeDataString(termo)}&limit=100");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement itens = await LerArrayAsync(resposta);
        itens.EnumerateArray().Select(c => c.GetProperty("nome").GetString())
            .Should().Contain("Marabá");
    }

    [Fact(DisplayName = "CA-04: filtros uf+q são preservados no cursor (header Link de next)")]
    public async Task Cidades_FiltroMaisCursor_Persiste()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        // 'a' casa Marabá e Parauapebas (PA); Óbidos (PA, "obidos") NÃO casa.
        // limit=1 força paginação — exercita o filtro em cada página seguida.
        using HttpResponseMessage pagina1 = await GeoReferenceSeed.Obter(client, "/api/cidades?uf=PA&q=a&limit=1");
        pagina1.StatusCode.Should().Be(HttpStatusCode.OK);
        string? proximo = GeoReferenceSeed.ExtrairLink(pagina1, "next");
        proximo.Should().NotBeNull("há mais de uma cidade PA casando 'a'");
        proximo.Should().Contain("uf=PA").And.Contain("q=a");
        // Seguindo o cursor, o conjunto é {Marabá, Parauapebas}: Óbidos nunca aparece.
        List<string> nomes = [.. LerNomes(await LerArrayAsync(pagina1)), .. await ListaDaApi.NomesDasPaginasAsync(client, proximo)];
        nomes.Should().BeEquivalentTo(["Marabá", "Parauapebas"]);
        nomes.Should().NotContain("Óbidos");
    }

    [Fact(DisplayName = "#700: lista de cidades vem ordenada alfabeticamente por nome, estável ao paginar")]
    public async Task Cidades_OrdenadasAlfabeticamente()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        List<string> nomes = await ListaDaApi.NomesDasPaginasAsync(client, "/api/cidades?limit=2");

        // Ordem por nome_normalizado: maraba < obidos < parauapebas < sao paulo.
        nomes.Should().Equal("Marabá", "Óbidos", "Parauapebas", "São Paulo");
    }

    [Fact(DisplayName = "#700: cidade sem nome_normalizado ordena pelo fallback normalizado de nome")]
    public async Task Cidades_Ordenacao_NomeNormalizadoNulo_UsaFallbackDeNome()
    {
        await _cenario.GravarCidadesDoParaAsync(
            para => new CidadeBuilder(para, "PA", "1500800", "Acará").ComNomeNormalizado(""),
            para => new CidadeBuilder(para, "PA", "1501402", "Belém").ComNomeNormalizado("belem"),
            para => new CidadeBuilder(para, "PA", "1599998", "Zzz").ComNomeNormalizado(""));
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        List<string> nomes = await ListaDaApi.NomesDasPaginasAsync(client, "/api/cidades?limit=1");

        nomes.Should().Equal("Acará", "Belém", "Zzz");
    }

    [Fact(DisplayName = "#700: cidades homônimas (mesmo nome, UFs distintas) têm ordem estável por desempate de Id")]
    public async Task Cidades_Homonimas_OrdemEstavel()
    {
        await _cenario.GravarCidadesHomonimasAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        List<string> primeira = await ColetarCodigosAsync(client);
        List<string> segunda = await ColetarCodigosAsync(client);

        primeira.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        segunda.Should().Equal(primeira, "homônimos são desempatados por Id de forma estável entre execuções");
    }

    [Theory(DisplayName = "Segurança ILIKE: metacaractere de busca é tratado como literal (não casa tudo)")]
    [InlineData("%")]
    [InlineData("_")]
    public async Task Cidades_Busca_MetacaractereLiteral(string termo)
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        // Sem escape, "%"/"_" casariam quase tudo. Com o escape do reader, são texto
        // literal: nenhum nome contém '%' ou '_', então o resultado é vazio.
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(
            client, $"/api/cidades?q={Uri.EscapeDataString(termo)}&limit=100");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LerArrayAsync(resposta)).GetArrayLength().Should().Be(0);
    }

    [Fact(DisplayName = "CA-05: detalhe por código IBGE traz territorial + indicador 1:1 + _links")]
    public async Task Cidade_Detalhe_PorCodigoIbge()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, "/api/cidades/1500402");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        ConferirDadosTerritoriaisDeMaraba(doc.RootElement);
        ConferirIndicadorDeMaraba(doc.RootElement.GetProperty("indicador"));
        ConferirLinksDeMaraba(doc.RootElement.GetProperty("_links"));
    }

    [Fact(DisplayName = "CA-05: cidade sem indicador omite o campo indicador (null) sem erro")]
    public async Task Cidade_Detalhe_SemIndicador()
    {
        await _cenario.GravarCidadesDoParaAsync(
            para => new CidadeBuilder(para, "PA", "1500701", "Abel Figueiredo"));
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, "/api/cidades/1500701");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("indicador", out JsonElement indicador).Should().BeTrue();
        indicador.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "CA-06: código IBGE bem-formado e inexistente retorna 404")]
    public async Task Cidade_Detalhe_Inexistente_404()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, "/api/cidades/9999999");
        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory(DisplayName = "CA-06: código IBGE malformado (≠7 dígitos) retorna 400")]
    [InlineData("/api/cidades/abc")]
    [InlineData("/api/cidades/123")]
    [InlineData("/api/cidades/15004020")]
    public async Task Cidade_Detalhe_Malformado_400(string rota)
    {
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, rota);
        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "CA-07: Accept vendor incompatível retorna 406; vendor cidade.v1 é aceito")]
    public async Task Cidades_VendorMime_406()
    {
        await _cenario.SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpRequestMessage incompativel = new(HttpMethod.Get, "/api/cidades");
        incompativel.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.uniplus.cidade.v2+json"));
        using HttpResponseMessage resp406 = await client.SendAsync(incompativel);
        resp406.StatusCode.Should().Be(HttpStatusCode.NotAcceptable);

        using HttpRequestMessage compativel = new(HttpMethod.Get, "/api/cidades");
        compativel.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.uniplus.cidade.v1+json"));
        using HttpResponseMessage resp200 = await client.SendAsync(compativel);
        resp200.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "CA-08: termo de busca acima do limite retorna 400")]
    public async Task Cidades_Busca_Longa_400()
    {
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        string termoLongo = new('a', 257);
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, $"/api/cidades?q={termoLongo}");
        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Busca sobre nome_normalizado null: cidade não aparece na busca, mas aparece sem filtro")]
    public async Task Cidades_Busca_NomeNormalizadoNulo()
    {
        // nomeNormalizado explicitamente vazio → normalizado para null pela entidade.
        await _cenario.GravarCidadesDoParaAsync(
            para => new CidadeBuilder(para, "PA", "1500800", "Acará").ComNomeNormalizado(""));
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage busca = await GeoReferenceSeed.Obter(client, "/api/cidades?q=acara&limit=100");
        (await LerArrayAsync(busca)).GetArrayLength().Should().Be(0, "ILIKE sobre coluna NULL não casa");

        using HttpResponseMessage semFiltro = await GeoReferenceSeed.Obter(client, "/api/cidades?limit=100");
        (await LerArrayAsync(semFiltro)).EnumerateArray()
            .Select(c => c.GetProperty("codigoIbge").GetString())
            .Should().Contain("1500800", "sem filtro a cidade aparece normalmente");
    }

    [Fact(DisplayName = "ADR-0092: cidade stale (vigente=false) não aparece na lista nem no detalhe")]
    public async Task Cidades_NaoVigente_NaoVaza()
    {
        await _cenario.GravarCidadesDoParaAsync(
            para => new CidadeBuilder(para, "PA", "1500402", "Marabá"),
            para => new CidadeBuilder(para, "PA", "1599999", "Obsoleta").ForaDeVigencia());
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage lista = await GeoReferenceSeed.Obter(client, "/api/cidades?limit=100");
        (await LerArrayAsync(lista)).EnumerateArray()
            .Select(c => c.GetProperty("codigoIbge").GetString())
            .Should().Contain("1500402").And.NotContain("1599999");

        using HttpResponseMessage detalhe = await GeoReferenceSeed.Obter(client, "/api/cidades/1599999");
        detalhe.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void ConferirDadosTerritoriaisDeMaraba(JsonElement raiz)
    {
        raiz.GetProperty("codigoIbge").GetString().Should().Be("1500402");
        raiz.GetProperty("nome").GetString().Should().Be("Marabá");
        raiz.GetProperty("uf").GetString().Should().Be("PA");
        raiz.GetProperty("mesorregiaoNome").GetString().Should().Be("Sudeste Paraense");
    }

    private static void ConferirIndicadorDeMaraba(JsonElement indicador)
    {
        indicador.GetProperty("gentilico").GetString().Should().Be("marabaense");
        indicador.GetProperty("idh").GetDecimal().Should().Be(0.668m);
        indicador.GetProperty("aniversario").GetString().Should().Be("05/04");
    }

    private static void ConferirLinksDeMaraba(JsonElement links)
    {
        links.GetProperty("self").GetString().Should().Be("/api/cidades/1500402");
        links.GetProperty("estado").GetString().Should().Be("/api/estados/PA");
        links.GetProperty("collection").GetString().Should().Be("/api/cidades");
    }

    // limit=1 força paginar ENTRE os homônimos — exercita o desempate por Id no cursor.
    private static async Task<List<string>> ColetarCodigosAsync(HttpClient client)
    {
        List<string> codigos = [];
        string? proximo = "/api/cidades?limit=1";
        while (proximo is not null)
        {
            using HttpResponseMessage pagina = await GeoReferenceSeed.Obter(client, proximo);
            pagina.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonElement itens = await LerArrayAsync(pagina);
            codigos.AddRange(itens.EnumerateArray().Select(c => c.GetProperty("codigoIbge").GetString()!));
            proximo = GeoReferenceSeed.ExtrairLink(pagina, "next");
        }

        return codigos;
    }

    private static async Task<JsonElement> LerArrayAsync(HttpResponseMessage resposta)
    {
        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static List<string> LerNomes(JsonElement array) =>
        [.. array.EnumerateArray().Select(item => item.GetProperty("nome").GetString()!)];
}
