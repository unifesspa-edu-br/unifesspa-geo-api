namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http;
using System.Text.Json;

using AwesomeAssertions;

/// <summary>Leitura dos campos de texto de uma lista devolvida pela API de Geo.</summary>
internal static class ListaDaApi
{
    public static async Task<List<string>> CamposAsync(HttpClient client, string rota, string campo)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, rota);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return [.. doc.RootElement.EnumerateArray().Select(item => item.GetProperty(campo).GetString()!)];
    }
}
