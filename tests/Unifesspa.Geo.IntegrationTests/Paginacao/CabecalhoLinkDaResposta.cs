namespace Unifesspa.Geo.IntegrationTests.Paginacao;

using System.Net.Http;

using Unifesspa.Geo.IntegrationTests.Api;

/// <summary>Leitura do cabeçalho <c>Link</c> de uma resposta paginada.</summary>
internal static class CabecalhoLinkDaResposta
{
    public static EnderecoDeNavegacao? Endereco(HttpResponseMessage resposta, string rel)
    {
        string? texto = GeoReferenceSeed.ExtrairLink(resposta, rel);
        return texto is null ? null : new EnderecoDeNavegacao(texto);
    }

    public static IReadOnlyList<string> Rels(HttpResponseMessage resposta)
    {
        const string Marca = "rel=\"";
        string cabecalho = string.Join(", ", resposta.Headers.GetValues("Link"));
        return [.. cabecalho.Split(", ")
            .Select(parte => parte[(parte.LastIndexOf(Marca, StringComparison.Ordinal) + Marca.Length)..^1])];
    }
}
