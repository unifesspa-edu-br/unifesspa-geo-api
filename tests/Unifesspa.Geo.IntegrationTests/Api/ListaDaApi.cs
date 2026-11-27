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

    // Segue o header Link de next a partir da rota dada (se houver), conferindo 200 em cada
    // página, e devolve os nomes de todas as páginas na ordem.
    public static async Task<List<string>> NomesDasPaginasAsync(HttpClient client, string? rota)
    {
        ArgumentNullException.ThrowIfNull(client);

        List<string> nomes = [];
        string? proximo = rota;
        while (proximo is not null)
        {
            using HttpResponseMessage pagina = await GeoReferenceSeed.Obter(client, proximo);
            pagina.StatusCode.Should().Be(HttpStatusCode.OK);
            using JsonDocument doc = JsonDocument.Parse(await pagina.Content.ReadAsStringAsync());
            nomes.AddRange(doc.RootElement.EnumerateArray().Select(item => item.GetProperty("nome").GetString()!));
            proximo = GeoReferenceSeed.ExtrairLink(pagina, "next");
        }

        return nomes;
    }
}
