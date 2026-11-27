namespace Unifesspa.Geo.Infrastructure.Core.Middleware;

using System.Diagnostics;
using System.Text.Json;

using Errors;

using Microsoft.AspNetCore.Http;

/// <summary>Escreve na resposta o <c>application/problem+json</c> (RFC 9457) do formato único da plataforma.</summary>
internal static class ProblemaNaResposta
{
    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    internal static Task EscreverAsync(
        HttpContext context,
        ProblemTypeUriFactory fabrica,
        DomainErrorMapping definicao,
        string detalhe)
    {
        Dictionary<string, object?> corpo = Corpo(fabrica, definicao);
        corpo["detail"] = detalhe;

        return EscreverAsync(context, definicao, corpo);
    }

    internal static async Task EscreverAsync(HttpContext context, DomainErrorMapping definicao, Dictionary<string, object?> corpo)
    {
        context.Response.StatusCode = definicao.Status;

        await context.Response
            .WriteAsJsonAsync(corpo, OpcoesJson, contentType: "application/problem+json")
            .ConfigureAwait(false);
    }

    internal static Dictionary<string, object?> Corpo(ProblemTypeUriFactory fabrica, DomainErrorMapping definicao) =>
        new(StringComparer.Ordinal)
        {
            ["type"] = fabrica.Build(definicao.Code).AbsoluteUri,
            ["title"] = definicao.Title,
            ["status"] = definicao.Status,
            ["instance"] = $"urn:uuid:{Guid.CreateVersion7()}",
            ["code"] = definicao.Code,
            ["traceId"] = Activity.Current?.TraceId.ToHexString() ?? Guid.CreateVersion7().ToString("N"),
        };
}
