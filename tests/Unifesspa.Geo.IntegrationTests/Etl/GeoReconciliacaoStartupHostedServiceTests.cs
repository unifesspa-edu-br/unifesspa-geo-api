namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Reconciliação na partida: o <see cref="GeoReconciliacaoStartupHostedService"/>
/// tenta adquirir a trava consultiva na partida e reconcilia execuções <c>EmAndamento</c>
/// órfãs como <c>Falhou</c> antes de o host aceitar disparos.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class GeoReconciliacaoStartupHostedServiceTests
{
    private readonly GeoPostgisFixture _fixture;

    public GeoReconciliacaoStartupHostedServiceTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Órfã EmAndamento é reconciliada no StartAsync quando a trava consultiva está livre, liberando novos disparos")]
    public async Task Startup_ComTravaLivre_ReconciliaOrfa_E_LiberaDisparos()
    {
        await LimparAsync();
        DateTimeOffset agora = TimeProvider.System.GetUtcNow();
        Guid orfa = await CriarEmAndamentoAsync(agora - TimeSpan.FromMinutes(5));

        GeoReconciliacaoStartupHostedService hosted = CriarHostedService();
        await hosted.StartAsync(CancellationToken.None);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == orfa);
        execucao.Status.Should().Be(StatusImportacao.Falhou, "a órfã é reconciliada antes de aceitar disparos");
        execucao.ConcluidoEm.Should().NotBeNull();

        Guid novo = await CriarEmAndamentoAsync(agora);
        novo.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "Execução EmAndamento é preservada no StartAsync quando a trava consultiva está ocupada")]
    public async Task Startup_ComTravaOcupada_PreservaExecucaoEmAndamento()
    {
        await LimparAsync();
        DateTimeOffset agora = TimeProvider.System.GetUtcNow();
        Guid ativa = await CriarEmAndamentoAsync(agora);

        await using (await TravaDaCargaParaTeste.AdquirirAsync(_fixture.ConnectionString))
        {
            GeoReconciliacaoStartupHostedService hosted = CriarHostedService();
            await hosted.StartAsync(CancellationToken.None);
        }

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == ativa);
        execucao.Status.Should().Be(StatusImportacao.EmAndamento, "a execução cuja trava está com outra sessão não é tocada");
    }

    private GeoReconciliacaoStartupHostedService CriarHostedService()
    {
        ServiceCollection services = new();
        services.AddScoped(_ => _fixture.CreateDbContext());
        ServiceProvider provider = services.BuildServiceProvider();

        return new GeoReconciliacaoStartupHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TravaDaCargaParaTeste.Criar(_fixture.ConnectionString),
            new FabricaDeFonteFixa(),
            new ArquivoDoPacoteDne(Path.Combine(Path.GetTempPath(), $"pacotes-dne-{Guid.NewGuid():N}")),
            TimeProvider.System,
            NullLogger<GeoReconciliacaoStartupHostedService>.Instance);
    }

    private async Task<Guid> CriarEmAndamentoAsync(DateTimeOffset iniciadoEm)
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = GeoImportacaoExecucao.Iniciar(PacotesDeTeste.Recebido("202601"), "teste", iniciadoEm).Value!;
        ctx.ImportacaoExecucoes.Add(execucao);
        await ctx.SaveChangesAsync();
        return execucao.Id;
    }

    private async Task LimparAsync()
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_importacao_execucao");
    }
}
