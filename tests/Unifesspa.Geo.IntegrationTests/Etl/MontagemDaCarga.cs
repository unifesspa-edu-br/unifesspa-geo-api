namespace Unifesspa.Geo.IntegrationTests.Etl;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Observability;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Monta a carga real sobre o banco da coleção, com a fonte, o relógio e os ganchos escolhidos
/// pelo teste. O cache de CEP e os logs ficam expostos para conferência.
/// </summary>
internal sealed class MontagemDaCarga : IDisposable
{
    private readonly GeoPostgisFixture _fixture;
    private readonly GeoEtlMetrics _metricas = new();

    public MontagemDaCarga(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public IGeoCepCacheInvalidador Cache { get; } = Substitute.For<IGeoCepCacheInvalidador>();

    public RegistroDeLogs Logs { get; } = new();

    public Func<IGeoImportador, IGeoImportador>? DecorarTopo { get; init; }

    public Func<IGeoImportadorLocalidades, IGeoImportadorLocalidades>? DecorarFolhas { get; init; }

    public TimeProvider RelogioDaCarga { get; init; } = new RelogioFixo();

    public void Dispose() => _metricas.Dispose();

    public async Task<Guid> RegistrarExecucaoAsync(string versao)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext(RelogioDaCarga);
        GeoImportacaoExecucao execucao = GeoImportacaoExecucao.Iniciar(PacotesDeTeste.Recebido(versao), "teste", RelogioDaCarga.GetUtcNow()).Value!;
        contexto.ImportacaoExecucoes.Add(execucao);
        await contexto.SaveChangesAsync();
        return execucao.Id;
    }

    public async Task ExecutarAsync(Guid execucaoId, IFonteDaExecucaoDne fabrica)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext(RelogioDaCarga);
        await ExecutarAsync(execucaoId, fabrica, contexto);
    }

    public async Task ExecutarAsync(Guid execucaoId, IFonteDaExecucaoDne fabrica, GeoDbContext contexto)
    {
        CargaReservada carga = await TravaDaCargaParaTeste.ReservarAsync(_fixture.ConnectionString, execucaoId);
        await Montar(contexto, fabrica).ExecutarAsync(carga, CancellationToken.None);
    }

    public async Task<Guid> PublicarAsync(IFonteDaCargaDne fonte)
    {
        Guid id = await RegistrarExecucaoAsync(fonte.Versao);
        await ExecutarAsync(id, new FabricaDeFonteFixa(fonte));
        return id;
    }

    private CargaDaImportacao Montar(GeoDbContext contexto, IFonteDaExecucaoDne fabrica)
    {
        IConfiguration configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:GeoDb"] = _fixture.ConnectionString })
            .Build();

        return new CargaDaImportacao(
            contexto,
            MontarImportadorDaVersao(contexto, fabrica, configuracao),
            MontarFinalizacao(fabrica),
            RelogioDaCarga,
            Options.Create(new EtlOpcoes()),
            Logs.Para<CargaDaImportacao>());
    }

    private ImportadorDaVersaoDne MontarImportadorDaVersao(GeoDbContext contexto, IFonteDaExecucaoDne fabrica, IConfiguration configuracao)
    {
        IGeoImportador topo = new GeoImportadorPaisEstadoCidade(contexto, Logs.Para<GeoImportadorPaisEstadoCidade>());
        ImportadorDeLogradourosPorCopia logradouro = new(contexto, configuracao, RelogioDaCarga, Logs.Para<ImportadorDeLogradourosPorCopia>());
        IGeoImportadorLocalidades folhas = new GeoImportadorLocalidades(
            new GeoImportadorDistritoBairro(contexto), logradouro, Logs.Para<GeoImportadorLocalidades>());

        return new ImportadorDaVersaoDne(
            contexto,
            DecorarTopo?.Invoke(topo) ?? topo,
            DecorarFolhas?.Invoke(folhas) ?? folhas,
            fabrica,
            RelogioDaCarga);
    }

    private FinalizacaoDaCargaDne MontarFinalizacao(IFonteDaExecucaoDne fabrica)
    {
        ServiceCollection servicos = new();
        servicos.AddScoped(_ => _fixture.CreateDbContext(RelogioDaCarga));
        ServiceProvider provedor = servicos.BuildServiceProvider();

        return new FinalizacaoDaCargaDne(
            provedor.GetRequiredService<IServiceScopeFactory>(),
            new Lazy<IGeoCepCacheInvalidador>(() => Cache),
            _metricas,
            fabrica,
            RelogioDaCarga,
            Logs.Para<FinalizacaoDaCargaDne>());
    }
}
