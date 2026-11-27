namespace Unifesspa.Geo.IntegrationTests.Paginacao;

using System.Net.Http;
using System.Text.Json;

using Unifesspa.Geo.IntegrationTests.Api;

/// <summary>Uma página de distritos lida da API: os identificadores na ordem entregue e os links de navegação.</summary>
internal sealed record PaginaDeDistritos(
    IReadOnlyList<Guid> Ids,
    EnderecoDeNavegacao? Anterior,
    EnderecoDeNavegacao? Seguinte)
{
    public static async Task<PaginaDeDistritos> LerAsync(HttpClient cliente, string rota)
    {
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(cliente, rota);
        resposta.EnsureSuccessStatusCode();
        using JsonDocument corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return new PaginaDeDistritos(
            [.. corpo.RootElement.EnumerateArray().Select(item => item.GetProperty("id").GetGuid())],
            CabecalhoLinkDaResposta.Endereco(resposta, "prev"),
            CabecalhoLinkDaResposta.Endereco(resposta, "next"));
    }
}
