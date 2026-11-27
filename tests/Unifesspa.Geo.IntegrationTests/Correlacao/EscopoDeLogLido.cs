namespace Unifesspa.Geo.IntegrationTests.Correlacao;

using Serilog;
using Serilog.Core;
using Serilog.Events;

/// <summary>
/// Lê a propriedade <c>CorrelationId</c> que o escopo de log em vigor acrescentaria a um registro emitido agora.
/// </summary>
internal sealed class EscopoDeLogLido
{
    private readonly Sumidouro _sumidouro = new();
    private readonly Logger _logger;

    public EscopoDeLogLido()
    {
        _logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(_sumidouro)
            .CreateLogger();
    }

    /// <summary>Valor do <c>CorrelationId</c> no escopo atual, ou nulo se o escopo não tem a propriedade.</summary>
    public string? CorrelationIdAgora()
    {
        _logger.Information("leitura do escopo");
        LogEvent evento = _sumidouro.Ultimo!;
        return evento.Properties.TryGetValue("CorrelationId", out LogEventPropertyValue? valor)
            ? ((ScalarValue)valor).Value as string
            : null;
    }

    private sealed class Sumidouro : ILogEventSink
    {
        public LogEvent? Ultimo { get; private set; }

        public void Emit(LogEvent logEvent)
        {
            Ultimo = logEvent;
        }
    }
}
