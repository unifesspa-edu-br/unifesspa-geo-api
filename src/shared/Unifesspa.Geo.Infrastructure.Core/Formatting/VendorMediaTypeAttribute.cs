namespace Unifesspa.Geo.Infrastructure.Core.Formatting;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Errors;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

/// <summary>
/// Action filter que negocia <c>application/vnd.uniplus.&lt;resource&gt;.v&lt;N&gt;+json</c>
/// no header <c>Accept</c>, conforme ADR-0028. <c>application/json</c>, <c>*/*</c>
/// ou <c>Accept</c> ausente caem para a versão mais recente (latest); versões
/// inexistentes resultam em 406 com extension <c>available_versions</c> e
/// o <c>Content-Type</c> da resposta é fixado na versão efetivamente aceita.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
[SuppressMessage(
    "Performance",
    "CA1813:Avoid unsealed attributes",
    Justification = "ASP.NET Core resolve filtros por tipo concreto via reflection; manter unsealed permite extensão controlada por subclasses específicas de recurso, sem quebrar a descoberta de filtros.")]
public class VendorMediaTypeAttribute : ActionFilterAttribute
{
    private const string ResponseContextKey = "__GeoVendorMediaTypeAccepted";

    /// <summary>Identificador do recurso na vendor MIME (ex.: <c>edital</c>).</summary>
    public string Resource { get; init; } = string.Empty;

    /// <summary>Versões inteiras aceitas. A última posição é o latest usado como fallback.</summary>
#pragma warning disable CA1819 // Properties should not return arrays
    public int[] Versions { get; init; } = [];
#pragma warning restore CA1819

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExigirConfiguracao();

        if (VersaoNegociada(context.HttpContext.Request) is { } versao)
            StoreAcceptedVersion(context.HttpContext, versao);
        else
            WriteNotAcceptable(context);
    }

    public override void OnResultExecuting(ResultExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.HttpContext.Items.TryGetValue(ResponseContextKey, out object? value)
            && value is int version
            && !IsProblemDetailsResult(context.Result))
        {
            AnunciarVersaoNaResposta(context.HttpContext.Response, version);
        }
    }

    private void ExigirConfiguracao()
    {
        if (string.IsNullOrEmpty(Resource) || Versions.Length == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(VendorMediaTypeAttribute)} requer Resource e Versions configurados.");
        }
    }

    // Sem preferência (Accept ausente ou em branco) vale a versão mais recente; um Accept que não
    // se lê, ou que nenhuma versão atende, não tem versão e dá 406.
    private int? VersaoNegociada(HttpRequest request)
    {
        if (NaoHaPreferenciaDeVersao(request, out Microsoft.Extensions.Primitives.StringValues valores))
            return Versions[^1];

        return MediaTypeHeaderValue.TryParseList(valores, out IList<MediaTypeHeaderValue>? pedidos)
            ? new NegociacaoDaVersaoDeMidia(Resource, Versions).VersaoAceita(pedidos)
            : null;
    }

    private static bool NaoHaPreferenciaDeVersao(HttpRequest request, out Microsoft.Extensions.Primitives.StringValues valores) =>
        !request.Headers.TryGetValue(HeaderNames.Accept, out valores)
        || valores.Count == 0
        || valores.All(static valor => string.IsNullOrWhiteSpace(valor));

    private void AnunciarVersaoNaResposta(HttpResponse response, int version)
    {
        response.ContentType = BuildVendorMime(Resource, version);

        // RFC 9110 §12.5.5: respostas que variam por content negotiation devem
        // declarar Vary: Accept para que caches compartilhados (CDN, Traefik,
        // reverse proxy) não sirvam representação de uma versão para um cliente
        // que pediu outra. Sem isso, /api/editais cacheado como v1 seria
        // replayado a um cliente que pediu v2.
        AppendAcceptToVaryIfMissing(response);
    }

    private static void AppendAcceptToVaryIfMissing(HttpResponse response)
    {
        Microsoft.Extensions.Primitives.StringValues vary = response.Headers.Vary;
        bool alreadyVariesByAccept = vary.Any(static value =>
            value is not null
            && value.Split(',').Any(static token =>
                token.Trim().Equals("Accept", StringComparison.OrdinalIgnoreCase)));

        if (!alreadyVariesByAccept)
            response.Headers.Append(HeaderNames.Vary, "Accept");
    }

    // RFC 9457 §3: respostas de erro mantêm Content-Type application/problem+json. O wire format
    // do erro não muda com o versionamento por vendor MIME — sobrescrever quebraria clientes que
    // despacham por content-type para tratar ProblemDetails.
    private static bool IsProblemDetailsResult(IActionResult result) =>
        result switch
        {
            ObjectResult { Value: ProblemDetails } => true,
            ObjectResult objectResult when objectResult.ContentTypes.Any(static ct =>
                string.Equals(ct, "application/problem+json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ct, "application/problem+xml", StringComparison.OrdinalIgnoreCase)) => true,
            _ => false,
        };

    private static void StoreAcceptedVersion(HttpContext httpContext, int version) =>
        httpContext.Items[ResponseContextKey] = version;

    private static string BuildVendorMime(string resource, int version) =>
        string.Create(CultureInfo.InvariantCulture, $"{NegociacaoDaVersaoDeMidia.PrefixoDoFornecedor}{resource}.v{version}{NegociacaoDaVersaoDeMidia.SufixoJson}");

    private void WriteNotAcceptable(ActionExecutingContext context)
    {
        DomainErrorMapping definicao = ErrosDaFronteiraHttp.VersaoDeMidiaNaoSuportada;
        Uri tipo = context.HttpContext.RequestServices.GetRequiredService<ProblemTypeUriFactory>().Build(definicao.Code);
        ProblemDetails problema = definicao.ComoProblema(
            tipo,
            $"Nenhuma versão suportada pelo recurso '{Resource}' foi aceita pelo cliente.",
            [new("available_versions", Versions)]);

        context.Result = new ObjectResult(problema)
        {
            StatusCode = definicao.Status,
            ContentTypes = { "application/problem+json" },
        };
    }
}
