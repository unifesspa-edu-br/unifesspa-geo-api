namespace Unifesspa.Geo.IntegrationTests;

using System.IO;
using System.Net;
using System.Text.Json;

using AwesomeAssertions;

using Infrastructure;

using TestSupport;

/// <summary>
/// Smoke + drift check do endpoint <c>/openapi/geo.json</c>: runtime gera a spec
/// OpenAPI 3.x do módulo Geo e compara com o contrato versionado em
/// <c>contracts/openapi.geo.json</c> (ADR-0030).
/// </summary>
public sealed class OpenApiEndpointTests : IClassFixture<GeoOpenApiFactory>
{
    private const string CaminhoDoContrato = "contracts/openapi.geo.json";

    private readonly GeoOpenApiFactory _factory;

    public OpenApiEndpointTests(GeoOpenApiFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "GET /openapi/geo.json retorna 200 com documento OpenAPI válido e metadata Uni+")]
    public async Task GetOpenApiDocument_RetornaSpecJsonComMetadataInjetada()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/openapi/geo.json", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;

        AssertMetadataDoDocumento(root);
        AssertEsquemaDeSegurancaBearer(root);
        AssertSegurancaDasConsultasDeEstados(root);
        AssertSegurancaDaImportacao(root);
    }

    [Fact(DisplayName = "POST de importação declara o corpo application/zip binário e as respostas do envio do pacote")]
    public async Task EnvioDoPacote_DeclaraCorpoZipBinarioERespostas()
    {
        using JsonDocument doc = await LerSpecAsync();
        JsonElement post = doc.RootElement.GetProperty("paths").GetProperty("/api/admin/geo/importacoes").GetProperty("post");

        JsonElement corpo = post.GetProperty("requestBody");
        corpo.GetProperty("required").GetBoolean().Should().BeTrue();
        corpo.GetProperty("content").EnumerateObject().Select(c => c.Name).Should().Equal("application/zip");
        JsonElement schema = corpo.GetProperty("content").GetProperty("application/zip").GetProperty("schema");
        schema.GetProperty("type").GetString().Should().Be("string");
        schema.GetProperty("format").GetString().Should().Be("binary");
        post.GetProperty("responses").EnumerateObject().Select(r => r.Name)
            .Should().BeEquivalentTo("200", "202", "401", "403", "409", "413", "415", "422", "500", "503");
    }

    private static void AssertMetadataDoDocumento(JsonElement root)
    {
        root.GetProperty("openapi").GetString().Should().StartWith("3.");
        root.GetProperty("info").GetProperty("title").GetString().Should().Be("Uni+ — Módulo Geo");
        root.GetProperty("info").GetProperty("version").GetString().Should().Be("1.0.0");
    }

    private static void AssertEsquemaDeSegurancaBearer(JsonElement root)
    {
        JsonElement bearer = root
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");
        bearer.GetProperty("type").GetString().Should().Be("http");
        bearer.GetProperty("scheme").GetString().Should().Be("bearer");
    }

    private static void AssertSegurancaDasConsultasDeEstados(JsonElement root)
    {
        JsonElement estadosGet = root.GetProperty("paths").GetProperty("/api/estados").GetProperty("get");
        estadosGet.GetProperty("security")[0].GetProperty("Bearer").GetArrayLength().Should().Be(0);
        estadosGet.GetProperty("responses").TryGetProperty("401", out _).Should().BeTrue();
        estadosGet.GetProperty("responses").TryGetProperty("403", out _).Should().BeFalse(
            "consultas Geo exigem token do realm, mas nao role administrativa");
    }

    private static void AssertSegurancaDaImportacao(JsonElement root)
    {
        JsonElement adminPost = root.GetProperty("paths").GetProperty("/api/admin/geo/importacoes").GetProperty("post");
        adminPost.GetProperty("security")[0].GetProperty("Bearer").GetArrayLength().Should().Be(0);
        adminPost.GetProperty("responses").TryGetProperty("403", out _).Should().BeTrue(
            "operacoes de administracao exigem role");
    }

    private async Task<JsonDocument> LerSpecAsync()
    {
        using HttpClient client = _factory.CreateClient();
        return JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/geo.json", UriKind.Relative)));
    }

    [Fact(DisplayName = "Spec runtime de Geo bate com o contrato versionado em contracts/")]
    public async Task SpecRuntime_DeveCasarComContratoVersionado()
    {
        using HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(new Uri("/openapi/geo.json", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        string obtido = NormalizeJson(await response.Content.ReadAsStringAsync()) + "\n";
        string versionado = CaminhoNoRepositorio.Resolver(CaminhoDoContrato);
        string esperado = File.Exists(versionado) ? (await File.ReadAllTextAsync(versionado)).ReplaceLineEndings("\n") : string.Empty;

        if (!string.Equals(obtido, esperado, StringComparison.Ordinal))
        {
            string caminho = ResultadoDeConferencia.Gravar(Path.GetFileName(versionado), obtido);
            Assert.Fail($"O contrato OpenAPI gerado difere de {CaminhoDoContrato} (ADR-0030).\n{ResultadoDeConferencia.Mensagem(caminho, versionado)}");
        }
    }

    private static string NormalizeJson(string raw)
    {
        using JsonDocument document = JsonDocument.Parse(raw);
        return JsonSerializer.Serialize(document.RootElement, JsonNormalizationOptions);
    }

    private static readonly JsonSerializerOptions JsonNormalizationOptions = new()
    {
        WriteIndented = true,
    };
}
