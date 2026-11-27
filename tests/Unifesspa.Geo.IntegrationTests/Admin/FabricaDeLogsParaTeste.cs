namespace Unifesspa.Geo.IntegrationTests.Admin;

using Microsoft.Extensions.Logging;

using Unifesspa.Geo.IntegrationTests.Etl;

/// <summary>Fábrica de registros de log que guarda tudo em memória, para o teste procurar o que não deve aparecer.</summary>
internal sealed class FabricaDeLogsParaTeste : ILoggerFactory
{
    private readonly RegistroDeLogs _registro;

    public FabricaDeLogsParaTeste(RegistroDeLogs registro)
    {
        _registro = registro;
    }

    public ILogger CreateLogger(string categoryName) => _registro.Para<object>();

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}
