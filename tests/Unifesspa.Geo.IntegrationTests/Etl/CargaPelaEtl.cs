namespace Unifesspa.Geo.IntegrationTests.Etl;

using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>Executa a carga de país, estado e cidade pelo importador real, sobre o banco da coleção.</summary>
internal static class CargaPelaEtl
{
    public static async Task<RelatorioImportacao> ExecutarAsync(GeoPostgisFixture fixture, IGeoFonteDados fonte)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        await using GeoDbContext ctx = fixture.CreateDbContext();
        GeoImportadorPaisEstadoCidade importador = new(ctx, NullLogger<GeoImportadorPaisEstadoCidade>.Instance);
        return await importador.ImportarAsync(fonte, CancellationToken.None);
    }
}
