namespace Unifesspa.Geo.IntegrationTests.Auth;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Fixtures.Authentication;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// O perfil do usuário autenticado devolve só identificador, nome, e-mail, papéis e instante;
/// CPF e nome social, mesmo presentes no token, não saem na resposta.
/// </summary>
public sealed class PerfilDoUsuarioAutenticadoTests : IClassFixture<GeoOpenApiFactory>
{
    private const string Rota = "/api/profile/me";
    private const string CpfFicticio = "52998224725";
    private const string NomeSocialFicticio = "Pessoa Ficticia de Teste";

    private readonly GeoOpenApiFactory _fabrica;

    public PerfilDoUsuarioAutenticadoTests(GeoOpenApiFactory fabrica)
    {
        _fabrica = fabrica;
    }

    [Fact(DisplayName = "Perfil com CPF e nome social no token responde 200 sem as propriedades cpf e nomeSocial")]
    public async Task PerfilComCpfENomeSocialNoToken_NaoDevolveOsDois()
    {
        using HttpClient cliente = ClienteAutenticadoComCpfENomeSocial();

        using HttpResponseMessage resposta = await cliente.GetAsync(new Uri(Rota, UriKind.Relative));
        string texto = await resposta.Content.ReadAsStringAsync();

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument corpo = JsonDocument.Parse(texto);
        corpo.RootElement.TryGetProperty("cpf", out _).Should().BeFalse();
        corpo.RootElement.TryGetProperty("nomeSocial", out _).Should().BeFalse();
        texto.Should().NotContain(CpfFicticio).And.NotContain(NomeSocialFicticio);
    }

    [Fact(DisplayName = "Perfil devolve identificador, nome, e-mail, papéis e instante")]
    public async Task Perfil_DevolveSoOsDadosDeIdentificacao()
    {
        using HttpClient cliente = ClienteAutenticadoComCpfENomeSocial();

        using HttpResponseMessage resposta = await cliente.GetAsync(new Uri(Rota, UriKind.Relative));
        using JsonDocument corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());

        string[] propriedades = [.. corpo.RootElement.EnumerateObject().Select(static p => p.Name)];
        propriedades.Should().BeEquivalentTo(["userId", "name", "email", "roles", "timestamp"]);
    }

    [Fact(DisplayName = "Perfil sem token responde 401")]
    public async Task PerfilSemToken_Responde401()
    {
        using HttpClient cliente = _fabrica.CreateClient();

        using HttpResponseMessage resposta = await cliente.GetAsync(new Uri(Rota, UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient ClienteAutenticadoComCpfENomeSocial()
    {
        HttpClient cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.AuthorizationScheme, TestAuthHandler.TokenValue);
        cliente.DefaultRequestHeaders.Add(TestAuthHandler.CpfHeader, CpfFicticio);
        cliente.DefaultRequestHeaders.Add(TestAuthHandler.NomeSocialHeader, NomeSocialFicticio);
        return cliente;
    }
}
