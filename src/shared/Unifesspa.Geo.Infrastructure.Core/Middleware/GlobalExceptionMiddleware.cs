namespace Unifesspa.Geo.Infrastructure.Core.Middleware;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using Errors;

using FluentValidation;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

public sealed partial class GlobalExceptionMiddleware
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly ProblemTypeUriFactory _problemTypeUriFactory;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        ProblemTypeUriFactory problemTypeUriFactory)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(problemTypeUriFactory);
        _next = next;
        _logger = logger;
        _problemTypeUriFactory = problemTypeUriFactory;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Global exception boundary: unhandled exceptions must be converted to RFC 9457 ProblemDetails before bubbling out of the pipeline.")]
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (ValidationException ex)
        {
            LogValidationError(_logger, context.Request.Path, ex);
            await EscreverRespostaValidacao(context, ex).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUnhandledError(_logger, context.Request.Path, ex);
            await EscreverRespostaErro(context).ConfigureAwait(false);
        }
    }

    private async Task EscreverRespostaValidacao(HttpContext context, ValidationException exception)
    {
        Dictionary<string, object?> body = CorpoBase(context, ErrosDaFronteiraHttp.ErroDeValidacao);
        // Invariante: e.ErrorMessage não deve conter PII nem o valor rejeitado.
        // Usar {PropertyValue} em templates FluentValidation viola essa restrição.
        body["errors"] = exception.Errors
            .Select(static e => new { field = e.PropertyName, code = e.ErrorCode, message = e.ErrorMessage })
            .ToArray();

        await context.Response
            .WriteAsJsonAsync(body, WebJsonOptions, contentType: "application/problem+json")
            .ConfigureAwait(false);
    }

    private async Task EscreverRespostaErro(HttpContext context)
    {
        Dictionary<string, object?> body = CorpoBase(context, ErrosDaFronteiraHttp.ErroInesperado);
        body["detail"] = "Ocorreu um erro inesperado. Tente novamente mais tarde.";

        await context.Response
            .WriteAsJsonAsync(body, WebJsonOptions, contentType: "application/problem+json")
            .ConfigureAwait(false);
    }

    private Dictionary<string, object?> CorpoBase(HttpContext context, DomainErrorMapping definicao)
    {
        context.Response.StatusCode = definicao.Status;

        return new Dictionary<string, object?>
        {
            ["type"] = _problemTypeUriFactory.Build(definicao.Code).AbsoluteUri,
            ["title"] = definicao.Title,
            ["status"] = definicao.Status,
            ["instance"] = $"urn:uuid:{Guid.CreateVersion7()}",
            ["code"] = definicao.Code,
            ["traceId"] = Activity.Current?.TraceId.ToHexString() ?? Guid.CreateVersion7().ToString("N"),
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Erro de validação no request {Path}")]
    private static partial void LogValidationError(ILogger logger, PathString path, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erro não tratado no request {Path}")]
    private static partial void LogUnhandledError(ILogger logger, PathString path, Exception ex);
}
