namespace Unifesspa.Geo.Infrastructure.Core.Middleware;

using Microsoft.AspNetCore.Http;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string LogContextProperty = "CorrelationId";
    public const int MaxCorrelationIdLength = 128;

    /// <summary>
    /// Nome do span attribute (Activity tag) que carrega o correlation_id no Tempo.
    /// Lido por <c>derivedFields</c> do Loki datasource em Grafana para o drill-down
    /// log → trace, fechando o ciclo log ↔ trace ↔ dashboard (ADR-0018).
    /// </summary>
    public const string ActivityTagName = "correlation_id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationIdWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        string correlationId = ObterOuGerarCorrelationId(context);

        writer.SetCorrelationId(correlationId);
        IdDeCorrelacao.MarcarNoRastreamento(correlationId);
        EscreverNaRespostaAoComecar(context.Response, correlationId);

        using (IdDeCorrelacao.AbrirEscopoDeLog(correlationId))
        {
            await _next(context).ConfigureAwait(false);
        }
    }

    // Postergar a escrita do header até o início do flush da resposta
    // garante que o valor sobreviva a qualquer mutação feita por
    // middlewares/filtros downstream e seja comprometido logo antes do
    // status line ser enviado ao cliente.
    private static void EscreverNaRespostaAoComecar(HttpResponse resposta, string correlationId)
    {
        resposta.OnStarting(() =>
        {
            resposta.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });
    }

    private static string ObterOuGerarCorrelationId(HttpContext context) =>
        IdDeCorrelacao.SeValido(context.Request.Headers[HeaderName].ToString()) ?? Guid.NewGuid().ToString("D");
}
