namespace Unifesspa.Geo.IntegrationTests.Auth;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Microsoft.IdentityModel.Tokens;

using Unifesspa.Geo.IntegrationTests.Fixtures.Hosting;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Exercita o pipeline produtivo JwtBearer contra Keycloak real: issuer/realm,
/// assinatura, lifetime, problem+json e roles vindas do token. Audience é opcional
/// por política da Geo API: qualquer aplicação UNIFESSPA do mesmo realm pode ler.
/// </summary>
[Collection(KeycloakContainerFixture.CollectionName)]
public sealed class JwtAuthenticationEndpointTests : IDisposable
{
    private readonly KeycloakContainerFixture _keycloak;
    private readonly GeoJwtApiFactory _factory;

    public JwtAuthenticationEndpointTests(KeycloakContainerFixture keycloak)
    {
        _keycloak = keycloak;
        _factory = new GeoJwtApiFactory(keycloak);
    }

    [Fact(DisplayName = "JWT real válido retorna /api/auth/me com claims e roles do Keycloak")]
    public async Task AuthMe_TokenJwtValido_RetornaClaims()
    {
        string token = await _keycloak.RequestAccessTokenAsync(
            KeycloakTestUsers.Admin.Username,
            KeycloakTestUsers.SharedPassword);

        using HttpClient client = _factory.CreateClient();
        using HttpRequestMessage request = AuthenticatedGet("/api/auth/me", token);

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = body.RootElement;

        root.GetProperty("email").GetString().Should().Be(KeycloakTestUsers.Admin.Email);
        root.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("roles")
            .EnumerateArray()
            .Select(static role => role.GetString())
            .Should()
            .Contain(KeycloakTestUsers.Admin.Role);
    }

    [Fact(DisplayName = "JWT real de outro client do mesmo realm autentica sem validar audience")]
    public async Task AuthMe_TokenJwtDeOutroClientDoMesmoRealm_Retorna200()
    {
        string token = await _keycloak.RequestAccessTokenAsync(
            KeycloakTestUsers.Admin.Username,
            KeycloakTestUsers.SharedPassword,
            KeycloakContainerFixture.BadAudienceClientId);

        using HttpClient client = _factory.CreateClient();
        using HttpRequestMessage request = AuthenticatedGet("/api/auth/me", token);

        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "JWT assinado por chave conhecida mas emitido por outro realm retorna 401 problem+json")]
    public async Task AuthMe_TokenJwtDeOutroRealm_Retorna401()
    {
        string token = CreateSignedTokenWithIssuer(
            issuer: new Uri(_keycloak.BaseAddress, "/realms/outro-realm").ToString());

        using HttpClient client = _factory.CreateClient();
        using HttpRequestMessage request = AuthenticatedGet("/api/auth/me", token);

        using HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "uniplus.auth.unauthorized");
        response.Headers.WwwAuthenticate.Select(static h => h.Scheme).Should().Contain("Bearer");
    }

    [Fact(DisplayName = "Endpoint protegido sem token retorna 401 problem+json")]
    public async Task AuthMe_SemToken_Retorna401()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative));

        await AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "uniplus.auth.unauthorized");
        response.Headers.WwwAuthenticate.Select(static h => h.Scheme).Should().Contain("Bearer");
    }

    [Fact(DisplayName = "Consulta Geo sem token retorna 401 problem+json")]
    public async Task ConsultaGeo_SemToken_Retorna401()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/estados", UriKind.Relative));

        await AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "uniplus.auth.unauthorized");
        response.Headers.WwwAuthenticate.Select(static h => h.Scheme).Should().Contain("Bearer");
    }

    [Fact(DisplayName = "JWT real autenticado sem role administrativa retorna 403 problem+json")]
    public async Task AdminGeoImportacoes_TokenJwtSemRoleAdministrativa_Retorna403()
    {
        string token = await _keycloak.RequestAccessTokenAsync(
            KeycloakTestUsers.Candidato.Username,
            KeycloakTestUsers.SharedPassword);

        using HttpClient client = _factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/admin/geo/importacoes")
        {
            Content = new StringContent("{\"versao\":\"202601\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Forbidden,
            "uniplus.auth.forbidden");
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    private static HttpRequestMessage AuthenticatedGet(string path, string token)
    {
        HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string CreateSignedTokenWithIssuer(string issuer)
    {
        DateTime now = DateTime.UtcNow;
        JwtSecurityToken token = new(
            issuer: issuer,
            audience: KeycloakContainerFixture.Audience,
            claims:
            [
                new Claim("sub", "usuario-outro-realm"),
                new Claim("name", "Usuario Outro Realm"),
                new Claim("email", "outro-realm@unifesspa.edu.br"),
            ],
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                KeycloakKnownTestKey.CreateSigningKey(),
                SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = body.RootElement;
        root.GetProperty("status").GetInt32().Should().Be((int)expectedStatus);
        root.GetProperty("code").GetString().Should().Be(expectedCode);
    }
}
