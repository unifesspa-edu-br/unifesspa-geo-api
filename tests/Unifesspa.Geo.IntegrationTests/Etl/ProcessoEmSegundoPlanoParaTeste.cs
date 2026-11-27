namespace Unifesspa.Geo.IntegrationTests.Etl;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Monta o processo em segundo plano da carga com um executor de teste.</summary>
internal static class ProcessoEmSegundoPlanoParaTeste
{
    public static GeoImportacaoBackgroundService Criar(IGeoImportacaoFila fila, IGeoImportacaoExecutor executor, ArquivoDoPacoteDne arquivos)
    {
        // Fábrica de escopos mínima: o esvaziamento da fila no StopAsync resolve IGeoImportacaoExecutor do escopo.
        IServiceProvider sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IGeoImportacaoExecutor)).Returns(executor);
        IServiceScope escopo = Substitute.For<IServiceScope>();
        escopo.ServiceProvider.Returns(sp);
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(escopo);

        return new GeoImportacaoBackgroundService(
            scopeFactory,
            fila,
            arquivos,
            NullLogger<GeoImportacaoBackgroundService>.Instance);
    }
}
