namespace Unifesspa.Geo.Infrastructure.Core.Observability;

/// <summary>
/// Catálogo canônico dos nomes de serviço do Geo — <em>single source of truth</em>
/// consumido pelo <c>Program.cs</c> ao chamar
/// <see cref="OpenTelemetryConfiguration.AdicionarObservabilidade"/> e
/// <see cref="Logging.SerilogConfiguration.ConfigurarSerilog(Serilog.LoggerConfiguration, Microsoft.Extensions.Configuration.IConfiguration, string?)"/>.
/// </summary>
/// <remarks>
/// <para>Centralizar os identificadores aqui garante por construção que o
/// <c>ServiceName</c> propagado pelo <see cref="Logging.ServiceNameEnricher"/>
/// (Serilog property) coincida com <c>service.name</c> do <c>Resource</c>
/// OpenTelemetry — esta é a invariante exigida pela ADR-0052 para evitar drift
/// entre logs (Loki/Console) e traces (Tempo).</para>
/// <para>O valor <c>uniplus-geo</c> é mantido durante a extração para preservar
/// dashboards, alertas e traces já existentes.</para>
/// </remarks>
public static class GeoServiceNames
{
    /// <summary>API Geo — localidades, endereçamento e georreferência nacional (PostGIS).</summary>
    public const string Geo = "uniplus-geo";
}
