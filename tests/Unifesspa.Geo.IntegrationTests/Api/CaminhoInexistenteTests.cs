namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// O caminho que nenhuma operação atende (rota que não existe, identificador de rota fora do formato)
/// responde 404 com o corpo de erro e o código comum da fronteira, sem repetir o caminho pedido; o método
/// que a rota não aceita, a falta de login e as rotas que existem continuam como estavam.
/// </summary>
public sealed class CaminhoInexistenteTests : IClassFixture<GeoOpenApiFactory>
{
    private const string BaseDoPortal = "https://unifesspa-edu-br.github.io/uniplus-developers/erros/";
    private const string Codigo = "uniplus.requisicao.caminho_inexistente";

    private readonly GeoOpenApiFactory _fabrica;

    public CaminhoInexistenteTests(GeoOpenApiFactory fabrica)
    {
        _fabrica = fabrica;
    }

    [Theory(DisplayName = "Rota que o Geo não declara responde 404 com o código comum e sem repetir o caminho")]
    [InlineData("/api/rota-que-o-geo-nao-declara-x7q")]
    [InlineData("/health/x7q")]
    [InlineData("/api/cidades/1500602/x7q")]
    public async Task RotaQueNaoExiste_Responde404ComOCodigoComum(string caminho) =>
        await ConferirCaminhoInexistenteAsync(caminho);

    [Fact(DisplayName = "Identificador de importação que não é um GUID responde 404 com o código comum e sem repetir o identificador")]
    public async Task IdentificadorForaDoFormato_Responde404ComOCodigoComum() =>
        await ConferirCaminhoInexistenteAsync("/api/admin/geo/importacoes/nao-e-guid-x7q");

    [Theory(DisplayName = "Método que a rota não aceita continua 405 com Allow e sem o código comum")]
    [InlineData("/api/cidades", "GET")]
    [InlineData("/api/admin/geo/importacoes/abc-x7q", "GET")]
    public async Task MetodoNaoAceito_Continua405(string caminho, string metodoPermitido)
    {
        using HttpClient cliente = _fabrica.CreateClient();

        using HttpResponseMessage resposta = await cliente.PostAsync(new Uri(caminho, UriKind.Relative), content: null);

        resposta.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        resposta.Content.Headers.Allow.Should().Contain(metodoPermitido);
        (await resposta.Content.ReadAsStringAsync()).Should().NotContain(Codigo);
    }

    [Fact(DisplayName = "Consulta sem token continua 401 com o código de não autenticado")]
    public async Task SemToken_Continua401()
    {
        using HttpClient cliente = _fabrica.CreateClient();

        using HttpResponseMessage resposta = await cliente.GetAsync(new Uri("/api/estados", UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("uniplus.auth.unauthorized").And.NotContain(Codigo);
    }

    [Theory(DisplayName = "Rotas que existem não mudam: saúde e contrato respondem 200")]
    [InlineData("/health/live")]
    [InlineData("/openapi/geo.json")]
    public async Task RotaQueExiste_NaoMuda(string caminho)
    {
        using HttpClient cliente = _fabrica.CreateClient();

        using HttpResponseMessage resposta = await cliente.GetAsync(new Uri(caminho, UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task ConferirCaminhoInexistenteAsync(string caminho)
    {
        using HttpClient cliente = _fabrica.CreateClient();

        using HttpResponseMessage resposta = await cliente.GetAsync(new Uri(caminho, UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        string texto = await resposta.Content.ReadAsStringAsync();
        using JsonDocument corpo = JsonDocument.Parse(texto);
        corpo.RootElement.GetProperty("code").GetString().Should().Be(Codigo);
        corpo.RootElement.GetProperty("type").GetString().Should().Be(BaseDoPortal + Codigo);
        corpo.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        corpo.RootElement.GetProperty("traceId").GetString().Should().HaveLength(32);
        texto.Should().NotContain("x7q");
    }
}
