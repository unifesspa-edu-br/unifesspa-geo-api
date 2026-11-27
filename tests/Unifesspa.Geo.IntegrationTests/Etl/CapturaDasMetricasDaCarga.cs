namespace Unifesspa.Geo.IntegrationTests.Etl;

using System.Diagnostics.Metrics;

using Unifesspa.Geo.Infrastructure.Core.Observability;

/// <summary>
/// Escuta apenas os instrumentos geo.etl.* do Meter do serviço Geo, copiando as tags do span
/// (não capturável em lambda) para um array. xUnit serializa os testes da coleção, então só a
/// carga sob teste emite enquanto a captura está viva.
/// </summary>
internal sealed class CapturaDasMetricasDaCarga : IDisposable
{
    private readonly MeterListener _listener = new();

    public CapturaDasMetricasDaCarga()
    {
        _listener.InstrumentPublished = HabilitarInstrumentosDaCarga;
        _listener.SetMeasurementEventCallback<long>((instrumento, medida, tags, estado) =>
            Inteiras.Add(new MedidaMetrica<long>(instrumento.Name, medida, tags.ToArray())));
        _listener.SetMeasurementEventCallback<double>((instrumento, medida, tags, estado) =>
            Decimais.Add(new MedidaMetrica<double>(instrumento.Name, medida, tags.ToArray())));
        _listener.Start();
    }

    public List<MedidaMetrica<long>> Inteiras { get; } = [];

    public List<MedidaMetrica<double>> Decimais { get; } = [];

    public static string? Status(KeyValuePair<string, object?>[] tags) =>
        tags.SingleOrDefault(t => string.Equals(t.Key, "status", StringComparison.Ordinal)).Value as string;

    public List<MedidaMetrica<long>> InteirasDe(string nome) =>
        [.. Inteiras.Where(m => string.Equals(m.Nome, nome, StringComparison.Ordinal))];

    public void Dispose() => _listener.Dispose();

    private static void HabilitarInstrumentosDaCarga(Instrument instrumento, MeterListener ouvinte)
    {
        if (string.Equals(instrumento.Meter.Name, GeoServiceNames.Geo, StringComparison.Ordinal) &&
            instrumento.Name.StartsWith("geo.etl.", StringComparison.Ordinal))
        {
            ouvinte.EnableMeasurementEvents(instrumento);
        }
    }
}

internal readonly record struct MedidaMetrica<T>(string Nome, T Valor, KeyValuePair<string, object?>[] Tags);
