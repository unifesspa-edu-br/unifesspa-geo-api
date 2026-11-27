namespace Unifesspa.Geo.Infrastructure.Core.Middleware;

using System.Diagnostics.CodeAnalysis;

using Errors;

using FluentValidation;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

public sealed partial class GlobalExceptionMiddleware
{
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
        catch (Exception ex)
        {
            await ResponderAFalhaAsync(context, ex).ConfigureAwait(false);
        }
    }

    private Task ResponderAFalhaAsync(HttpContext context, Exception falha) =>
        falha is ValidationException validacao
            ? ResponderValidacaoRecusadaAsync(context, validacao)
            : ResponderErroInesperadoAsync(context, falha);

    private async Task ResponderValidacaoRecusadaAsync(HttpContext context, ValidationException exception)
    {
        LogValidationError(_logger, context.Request.Path, exception);
        Dictionary<string, object?> corpo = ProblemaNaResposta.Corpo(_problemTypeUriFactory, ErrosDaFronteiraHttp.ErroDeValidacao);
        // Invariante: e.ErrorMessage não deve conter PII nem o valor rejeitado.
        // Usar {PropertyValue} em templates FluentValidation viola essa restrição.
        corpo["errors"] = exception.Errors
            .Select(static e => new { field = e.PropertyName, code = e.ErrorCode, message = e.ErrorMessage })
            .ToArray();

        await ProblemaNaResposta.EscreverAsync(context, ErrosDaFronteiraHttp.ErroDeValidacao, corpo).ConfigureAwait(false);
    }

    private Task ResponderErroInesperadoAsync(HttpContext context, Exception falha)
    {
        LogUnhandledError(_logger, context.Request.Path, falha);
        return ProblemaNaResposta.EscreverAsync(
            context,
            _problemTypeUriFactory,
            ErrosDaFronteiraHttp.ErroInesperado,
            "Ocorreu um erro inesperado. Tente novamente mais tarde.");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Erro de validação no request {Path}")]
    private static partial void LogValidationError(ILogger logger, PathString path, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erro não tratado no request {Path}")]
    private static partial void LogUnhandledError(ILogger logger, PathString path, Exception ex);
}
