namespace Unifesspa.Geo.Infrastructure.Core.Middleware;

using System.Collections.Immutable;
using System.Diagnostics;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Middleware de logging estruturado de requests HTTP.</summary>
/// <remarks>
/// Mascara valores de query string via <see cref="QueryStringMasker"/>, mas não segmentos
/// de path — PII em path segments vaza em camadas anteriores ao middleware (nginx, WAF, CDN,
/// cabeçalho Referer). Rotas devem usar identificadores opacos (UUID), nunca dados sensíveis
/// em path. Ver ADR-0019.
/// </remarks>
public sealed partial class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly QueryStringMasker _masker;
    private readonly ImmutableArray<string> _prefixosSilenciados;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger,
        QueryStringMasker masker,
        IOptions<RequestLoggingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(masker);
        ArgumentNullException.ThrowIfNull(options);
        _next = next;
        _logger = logger;
        _masker = masker;

        // ImmutableArray (não FrozenSet): matching é prefix+boundary, não igualdade.
        _prefixosSilenciados = options.Value.PrefixosSilenciados
            .Select(NormalizarPrefixo)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    // A falha que escapa do pipeline é registrada como Error e relançada (`throw;` preserva a
    // pilha para o GlobalExceptionMiddleware). Sem isso, ela apareceria como Information 200
    // no log (código de status padrão).
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        long inicio = Stopwatch.GetTimestamp();
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RegistrarConcluida(context, inicio, ex);
            throw;
        }

        RegistrarConcluida(context, inicio, null);
    }

    private void RegistrarConcluida(HttpContext context, long inicio, Exception? falha) =>
        Registrar(RequisicaoConcluida.De(context, _masker, inicio), falha);

    private void Registrar(RequisicaoConcluida r, Exception? falha)
    {
        if (falha is not null || r.Status >= 500)
        {
            LogRequestServerError(_logger, r.Metodo, r.Caminho, r.Consulta, r.Status, r.DuracaoMs, falha);
        }
        else if (r.Status >= 400)
        {
            LogRequestClientError(_logger, r.Metodo, r.Caminho, r.Consulta, r.Status, r.DuracaoMs);
        }
        else if (!DeveSilenciar(r.Caminho))
        {
            LogRequestSucesso(_logger, r.Metodo, r.Caminho, r.Consulta, r.Status, r.DuracaoMs);
        }
    }

    private bool DeveSilenciar(string path)
    {
        ReadOnlySpan<char> caminho = SemBarraFinal(path);
        foreach (string prefixo in _prefixosSilenciados)
        {
            if (CasaComPrefixo(caminho, prefixo))
            {
                return true;
            }
        }

        return false;
    }

    // Span evita alocação no hot path em requests com trailing slash.
    private static ReadOnlySpan<char> SemBarraFinal(string caminho)
    {
        ReadOnlySpan<char> span = caminho.AsSpan();
        return span.Length > 1 && span[^1] == '/' ? span.TrimEnd('/') : span;
    }

    private static bool CasaComPrefixo(ReadOnlySpan<char> caminho, string prefixo) =>
        caminho.Equals(prefixo, StringComparison.OrdinalIgnoreCase)
        || (caminho.Length > prefixo.Length
            && caminho.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)
            && caminho[prefixo.Length] == '/');

    private static string NormalizarPrefixo(string valor) =>
        valor.Length > 1 && valor.EndsWith('/') ? valor.TrimEnd('/') : valor;

    [LoggerMessage(Level = LogLevel.Information, Message = "HTTP {Method} {Path}{Query} respondeu {StatusCode} em {ElapsedMs}ms")]
    private static partial void LogRequestSucesso(ILogger logger, string method, string path, string query, int statusCode, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "HTTP {Method} {Path}{Query} respondeu {StatusCode} em {ElapsedMs}ms")]
    private static partial void LogRequestClientError(ILogger logger, string method, string path, string query, int statusCode, double elapsedMs);

    // Exception no último parâmetro: convenção do source generator do LoggerMessage —
    // vai para LogEvent.Exception (stack estruturada nos sinks), não para o template.
    [LoggerMessage(Level = LogLevel.Error, Message = "HTTP {Method} {Path}{Query} respondeu {StatusCode} em {ElapsedMs}ms")]
    private static partial void LogRequestServerError(ILogger logger, string method, string path, string query, int statusCode, double elapsedMs, Exception? ex);

    private readonly record struct RequisicaoConcluida(string Metodo, string Caminho, string Consulta, int Status, double DuracaoMs)
    {
        internal static RequisicaoConcluida De(HttpContext context, QueryStringMasker masker, long inicio)
        {
            double duracaoMs = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
            return new(
                context.Request.Method,
                context.Request.Path.HasValue ? context.Request.Path.Value! : "/",
                masker.Mascarar(context.Request.QueryString),
                context.Response.StatusCode,
                duracaoMs);
        }
    }
}
