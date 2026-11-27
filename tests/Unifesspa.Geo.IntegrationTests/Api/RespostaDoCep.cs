namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http;
using System.Text.Json;

using AwesomeAssertions;

/// <summary>Consulta de um CEP na API de Geo que confere o status 200 e devolve o corpo da resposta.</summary>
internal static class RespostaDoCep
{
    public static async Task<JsonElement> ResolverAsync(HttpClient client, string rota)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, rota);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
