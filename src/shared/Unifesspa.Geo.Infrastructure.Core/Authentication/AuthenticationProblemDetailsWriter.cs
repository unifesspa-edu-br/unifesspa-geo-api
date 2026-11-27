namespace Unifesspa.Geo.Infrastructure.Core.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

using Unifesspa.Geo.Infrastructure.Core.Errors;

/// <summary>
/// Writer compartilhado para respostas <c>application/problem+json</c> originadas
/// na camada de autenticação/autorização (RFC 9457). Centraliza shape, type URLs e
/// extensions (<c>code</c>, <c>traceId</c>, <c>instance</c>) para que JwtBearer
/// (produção) e handlers de teste produzam payloads byte-equivalentes.
/// </summary>
public static class AuthenticationProblemDetailsWriter
{
    private const string UnauthorizedDetail =
        "Requisição requer autenticação válida. Inclua um token Bearer não expirado.";
    private const string ForbiddenDetail =
        "Token autenticado, mas sem permissão para acessar este recurso.";

    /// <summary>
    /// Escreve um body 401 problem+json no <paramref name="httpContext"/>.
    /// Adiciona <c>WWW-Authenticate: Bearer</c> se ausente — RFC 7235 §4.1 e
    /// RFC 9110 §11.6.1 exigem o header em toda resposta 401.
    /// </summary>
    public static Task WriteUnauthorizedAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // Sem realm/error/error_description — política Uni+ não expõe motivo
        // de falha JWT no canal de resposta (LGPD + ADR-0034).
        if (!httpContext.Response.HasStarted
            && !httpContext.Response.Headers.ContainsKey(HeaderNames.WWWAuthenticate))
        {
            httpContext.Response.Headers.Append(HeaderNames.WWWAuthenticate, "Bearer");
        }

        return WriteAsync(httpContext, ErrosDaFronteiraHttp.NaoAutenticado, UnauthorizedDetail);
    }

    /// <summary>
    /// Escreve um body 403 problem+json no <paramref name="httpContext"/>.
    /// </summary>
    public static Task WriteForbiddenAsync(HttpContext httpContext) =>
        WriteAsync(httpContext, ErrosDaFronteiraHttp.AcessoNegado, ForbiddenDetail);

    private static async Task WriteAsync(
        HttpContext httpContext,
        DomainErrorMapping definicao,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.Response.HasStarted)
            return;

        httpContext.Response.StatusCode = definicao.Status;
        Uri tipo = httpContext.RequestServices.GetRequiredService<ProblemTypeUriFactory>().Build(definicao.Code);
        ProblemDetails problem = definicao.ComoProblema(tipo, detail);

        if (!await TryWriteComServicoAsync(httpContext, problem).ConfigureAwait(false))
            await EscreverSemOServicoAsync(httpContext, problem).ConfigureAwait(false);
    }

    // Caso AddProblemDetails não tenha sido registrado, escreve o corpo à mão preservando o
    // contrato application/problem+json. A sobrecarga de WriteAsJsonAsync com contentType é
    // obrigatória: a sem contentType reescreve o cabeçalho como "application/json; charset=utf-8"
    // mesmo depois de Response.ContentType ter sido definido, e quebra o contrato problem+json.
    private static Task EscreverSemOServicoAsync(HttpContext httpContext, ProblemDetails problem) =>
        httpContext.Response.WriteAsJsonAsync(
            problem,
            options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json");

    private static async Task<bool> TryWriteComServicoAsync(HttpContext httpContext, ProblemDetails problem)
    {
        IProblemDetailsService? service = httpContext.RequestServices.GetService<IProblemDetailsService>();
        if (service is null)
            return false;

        ProblemDetailsContext problemContext = new() { HttpContext = httpContext, ProblemDetails = problem };
        return await service.TryWriteAsync(problemContext).ConfigureAwait(false);
    }
}
