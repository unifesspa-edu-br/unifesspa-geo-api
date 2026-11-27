namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Fixtures.Authentication;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// As rotas de diagnóstico de cache e de fila não existem mais: mesmo quem tem o papel administrativo
/// recebe 404 com o código comum de caminho inexistente, e o contrato servido não lista o grupo.
/// </summary>
public sealed class RotasDeDiagnosticoRetiradasTests : IClassFixture<GeoOpenApiFactory>
{
    private const string CodigoDoCaminhoInexistente = "uniplus.requisicao.caminho_inexistente";
    private const string PrefixoRetirado = "/api/_smoke";

    private readonly GeoOpenApiFactory _fabrica;

    public RotasDeDiagnosticoRetiradasTests(GeoOpenApiFactory fabrica)
    {
        _fabrica = fabrica;
    }

    [Theory(DisplayName = "Rota de diagnóstico retirada responde 404 com o código de caminho inexistente para quem tem o papel admin")]
    [InlineData("GET", "/api/_smoke/cache/qualquer")]
    [InlineData("POST", "/api/_smoke/messaging/publish")]
    public async Task RotaRetirada_Responde404(string metodo, string caminho)
    {
        using HttpClient cliente = ClienteComPapelAdmin();
        using HttpRequestMessage requisicao = new(new HttpMethod(metodo), new Uri(caminho, UriKind.Relative));

        using HttpResponseMessage resposta = await cliente.SendAsync(requisicao);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        corpo.RootElement.GetProperty("code").GetString().Should().Be(CodigoDoCaminhoInexistente);
    }

    [Fact(DisplayName = "Contrato servido não lista nenhum caminho de diagnóstico sob /api/_smoke")]
    public async Task ContratoServido_NaoListaOsCaminhosRetirados()
    {
        using HttpClient cliente = _fabrica.CreateClient();

        string texto = await cliente.GetStringAsync(new Uri("/openapi/geo.json", UriKind.Relative));

        using JsonDocument documento = JsonDocument.Parse(texto);
        documento.RootElement.GetProperty("paths").EnumerateObject()
            .Select(caminho => caminho.Name)
            .Should().NotContain(nome => nome.StartsWith(PrefixoRetirado, StringComparison.Ordinal));
    }

    private HttpClient ClienteComPapelAdmin()
    {
        HttpClient cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.AuthorizationScheme,
            TestAuthHandler.TokenValue);
        cliente.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "admin");
        return cliente;
    }
}
