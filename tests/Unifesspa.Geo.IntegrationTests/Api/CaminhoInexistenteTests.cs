namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// O caminho que nenhuma operação atende (rota que não existe, identificador de rota fora do formato)
/// responde 404 com o corpo de erro e o código comum da fronteira, e o método que a rota não aceita responde 405 com
/// Allow e o código próprio, sem repetir o caminho nem o método pedidos; a falta de login e as rotas que existem
/// continuam como estavam.
/// </summary>
public sealed class CaminhoInexistenteTests : IClassFixture<GeoOpenApiFactory>
{
    private const string BaseDoPortal = "https://unifesspa-edu-br.github.io/uniplus-developers/erros/";
    private const string Codigo = "uniplus.requisicao.caminho_inexistente";
    private const string CodigoDoMetodoNaoAceito = "uniplus.requisicao.metodo_nao_aceito";

    private readonly GeoOpenApiFactory _fabrica;

    public CaminhoInexistenteTests(GeoOpenApiFactory fabrica)
    {
        _fabrica = fabrica;
    }

    [Theory(DisplayName = "Rota que o Geo não declara responde 404 com o código comum e sem repetir o caminho")]
    [InlineData("/api/rota-que-o-geo-nao-declara-x7q")]
    [InlineData("/health/x7q")]
    [InlineData("/api/cidades/1500602/x7q")]
    [InlineData("/openapi/inexistente-x7q.json")]
    [InlineData("/openapi/x7q%3Cb%3E.json")]
    public async Task RotaQueNaoExiste_Responde404ComOCodigoComum(string caminho) =>
        await ConferirCaminhoInexistenteAsync(caminho);

    [Fact(DisplayName = "Identificador de importação que não é um GUID responde 404 com o código comum e sem repetir o identificador")]
    public async Task IdentificadorForaDoFormato_Responde404ComOCodigoComum() =>
        await ConferirCaminhoInexistenteAsync("/api/admin/geo/importacoes/nao-e-guid-x7q");

    [Fact(DisplayName = "Documento de contrato com nome longo e codificado responde 404 com o código comum e sem repetir o nome")]
    public async Task DocumentoComNomeLongoECodificado_Responde404ComOCodigoComum() =>
        await ConferirCaminhoInexistenteAsync("/openapi/x7q" + new string('a', 500) + "%20%3Cb%3E.json");

    [Fact(DisplayName = "Documento de contrato com o nome publicado em outra grafia responde 404 com o código comum")]
    public async Task DocumentoComOutraGrafia_Responde404ComOCodigoComum() =>
        await ConferirCaminhoInexistenteAsync("/openapi/GEO.json");

    [Theory(DisplayName = "Método que a rota não aceita responde 405 com Allow e o código do método não aceito, sem repetir o caminho nem o método")]
    [InlineData("/api/cidades", "POST", "GET")]
    [InlineData("/api/admin/geo/importacoes/abc-x7q", "POST", "GET")]
    [InlineData("/api/estados", "DELETE", "GET")]
    [InlineData("/openapi/inexistente-x7q.json", "POST", "GET")]
    [InlineData("/openapi/geo.json", "POST", "GET")]
    public async Task MetodoNaoAceito_Responde405ComOCodigoEAllow(string caminho, string metodo, string metodoPermitido)
    {
        using HttpClient cliente = _fabrica.CreateClient();
        using HttpRequestMessage requisicao = new(new HttpMethod(metodo), new Uri(caminho, UriKind.Relative));

        using HttpResponseMessage resposta = await cliente.SendAsync(requisicao);

        resposta.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        resposta.Content.Headers.Allow.Should().Contain(metodoPermitido);
        string texto = await ConferirCorpoDoMetodoNaoAceitoAsync(resposta);
        texto.Should().NotContain("x7q").And.NotContain(metodo).And.NotContain(caminho);
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

    private static async Task<string> ConferirCorpoDoMetodoNaoAceitoAsync(HttpResponseMessage resposta)
    {
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        string texto = await resposta.Content.ReadAsStringAsync();
        using JsonDocument corpo = JsonDocument.Parse(texto);
        corpo.RootElement.GetProperty("code").GetString().Should().Be(CodigoDoMetodoNaoAceito);
        corpo.RootElement.GetProperty("type").GetString().Should().Be(BaseDoPortal + CodigoDoMetodoNaoAceito);
        corpo.RootElement.GetProperty("status").GetInt32().Should().Be(405);
        corpo.RootElement.GetProperty("traceId").GetString().Should().HaveLength(32);
        return texto;
    }
}
