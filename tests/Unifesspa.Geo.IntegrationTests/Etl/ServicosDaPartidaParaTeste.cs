namespace Unifesspa.Geo.IntegrationTests.Etl;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Monta os serviços do escopo que a reconciliação da partida resolve: o contexto do banco e o
/// encerramento das cargas abandonadas, com a trava e a fonte informadas.
/// </summary>
internal static class ServicosDaPartidaParaTeste
{
    public static IServiceScopeFactory Criar(
        GeoPostgisFixture fixture,
        IFonteDaExecucaoDne fonte,
        Action<IServiceCollection>? complemento = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(fonte);

        ServiceCollection servicos = new();
        servicos.AddScoped(_ => fixture.CreateDbContext());
        servicos.AddScoped(sp => new EncerramentoDasCargasAbandonadas(
            sp.GetRequiredService<GeoDbContext>(),
            fonte,
            TravaDaCargaParaTeste.Criar(fixture.ConnectionString),
            TimeProvider.System,
            NullLogger<EncerramentoDasCargasAbandonadas>.Instance));
        complemento?.Invoke(servicos);
        return servicos.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
