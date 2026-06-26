namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using System.Net.Http.Headers;

using Unifesspa.Geo.IntegrationTests.Fixtures.Authentication;

internal static class AuthenticatedHttpClient
{
    public static HttpClient CreateAuthenticatedClient(this GeoApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.AuthorizationScheme,
            TestAuthHandler.TokenValue);

        return client;
    }
}
