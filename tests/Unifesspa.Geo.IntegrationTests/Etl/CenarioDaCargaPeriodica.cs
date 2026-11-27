namespace Unifesspa.Geo.IntegrationTests.Etl;

using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
/// Apoio das classes de teste da atualização periódica da carga: monta o executor da carga
/// real sobre o banco da coleção, com um relógio de teste que avança a cada execução, e traz
/// as versões fictícias usadas pelos cenários. Usado por composição.
/// </summary>
internal sealed class CenarioDaCargaPeriodica
{
    public const string Maraba = "1500402";
    public const string SaoPaulo = "3550308";
    public const string CepMaraba = "68500000";
    public const string CepSaoPaulo = "01000000";

    public static readonly JsonSerializerOptions RelatorioJsonCamelCase =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly GeoPostgisFixture _fixture;

    public CenarioDaCargaPeriodica(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public RelogioFixo Relogio { get; } = new();

    /// <summary>Interceptadores do EF Core aplicados a todo contexto criado pelo cenário, inclusive o da finalização da carga.</summary>
    public IReadOnlyList<IInterceptor> Interceptadores { get; set; } = [];

    public GeoDbContext CriarContexto() => _fixture.CreateDbContext(Relogio, Interceptadores);

    public async Task<Guid> CriarExecucaoAsync(string versao)
    {
        await using GeoDbContext ctx = CriarContexto();
        GeoImportacaoExecucao execucao = GeoImportacaoExecucao
            .Iniciar(PacotesDeTeste.Recebido(versao), "teste", Relogio.GetUtcNow())
            .Value!;
        ctx.ImportacaoExecucoes.Add(execucao);
        await ctx.SaveChangesAsync();
        return execucao.Id;
    }

    /// <summary>Cria a execução da versão e a executa em seguida; devolve o identificador da execução.</summary>
    public async Task<Guid> ExecutarVersaoAsync(string versao, IFonteDaExecucaoDne fonteFactory, IGeoCepCacheInvalidador cache)
    {
        Guid id = await CriarExecucaoAsync(versao);
        await ExecutarAsync(id, fonteFactory, cache);
        return id;
    }

    public Task ExecutarAsync(Guid execucaoId, IFonteDaExecucaoDne fonteFactory, IGeoCepCacheInvalidador cache) =>
        ExecutarComLazyAsync(execucaoId, fonteFactory, new Lazy<IGeoCepCacheInvalidador>(() => cache));

    // Cada execução começa depois da anterior: a marcação de não vigentes compara os
    // carimbos gravados com o instante em que a carga iniciou.
    public async Task ExecutarComLazyAsync(Guid execucaoId, IFonteDaExecucaoDne fonteFactory, Lazy<IGeoCepCacheInvalidador> cacheLazy)
    {
        Relogio.Avancar(TimeSpan.FromMinutes(1));
        await using GeoDbContext ctx = CriarContexto();
        using GeoEtlMetrics metricas = new();
        CargaDaImportacao executor = CriarExecutor(ctx, fonteFactory, cacheLazy, metricas);
        CargaReservada carga = await ReservarAsync(execucaoId);
        await executor.ExecutarAsync(carga, CancellationToken.None);
    }

    public async Task<GeoImportacaoExecucao> LerExecucaoAsync(Guid execucaoId)
    {
        await using GeoDbContext ctx = CriarContexto();
        return await ctx.ImportacaoExecucoes.AsNoTracking().SingleAsync(e => e.Id == execucaoId);
    }

    public Task<CargaReservada> ReservarAsync(Guid execucaoId) =>
        TravaDaCargaParaTeste.ReservarAsync(_fixture.ConnectionString, execucaoId);

    public CargaDaImportacao CriarExecutor(
        GeoDbContext ctx,
        IFonteDaExecucaoDne fonteFactory,
        Lazy<IGeoCepCacheInvalidador> cacheLazy,
        GeoEtlMetrics metricas)
    {
        IConfiguration configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ConnectionStrings:GeoDb"] = _fixture.ConnectionString })
            .Build();

        return new CargaDaImportacao(
            ctx,
            CriarImportadorVersao(ctx, fonteFactory, configuracao),
            CriarFinalizacao(cacheLazy, metricas, fonteFactory),
            Relogio,
            Options.Create(new EtlOpcoes()),
            NullLogger<CargaDaImportacao>.Instance);
    }

    public async Task LimparAsync()
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE pais, logradouro_complemento, cep_grande_usuario, geo_importacao_execucao CASCADE");
    }

    public static FonteEmMemoria FonteCompleta(string versao)
    {
        FonteEmMemoria fonte = FonteSomenteMaraba(versao);
        AdicionarSaoPaulo(fonte);
        return fonte;
    }

    // Versão seguinte sem São Paulo (cidade + CEP somem) — exercita a marcação de não vigentes.
    public static FonteEmMemoria FonteSomenteMaraba(string versao)
    {
        FonteEmMemoria fonte = new() { Versao = versao };

        fonte.Paises.Add(DadosDne.Pais("BRA", "Brasil", "BR"));
        fonte.Estados.Add(new EstadoCruBuilder("PA", "Pará").ComCapital("Belém").NaFaixaDeCep("66000000", "68899999").Construir());
        fonte.EstadoIndicadores.Add(DadosDne.EstadoIndicador("PA", "15"));
        fonte.Cidades.Add(DadosDne.Cidade(Maraba, "Marabá", "PA", ddd: "94"));
        fonte.CidadeIds.Add(DadosDne.CidadeId(1, Maraba));
        fonte.Distritos.Add(DadosDne.Distrito(10, "Cidade Nova", 1));
        fonte.Logradouros.Add(new LogradouroCruBuilder(CepMaraba, "Rua A", 1).NoDistrito(10).Construir());

        return fonte;
    }

    private static void AdicionarSaoPaulo(FonteEmMemoria fonte)
    {
        fonte.Estados.Add(new EstadoCruBuilder("SP", "São Paulo").NaRegiao("Sudeste").ComCapital("São Paulo").NaFaixaDeCep("01000000", "19999999").Construir());
        fonte.EstadoIndicadores.Add(DadosDne.EstadoIndicador("SP", "35"));
        fonte.Cidades.Add(DadosDne.Cidade(SaoPaulo, "São Paulo", "SP", ddd: "11"));
        fonte.CidadeIds.Add(DadosDne.CidadeId(2, SaoPaulo));
        fonte.Distritos.Add(DadosDne.Distrito(20, "Centro", 2));
        fonte.Logradouros.Add(new LogradouroCruBuilder(CepSaoPaulo, "Av Paulista", 2).NoDistrito(20).Construir());
    }

    private ImportadorDaVersaoDne CriarImportadorVersao(
        GeoDbContext ctx,
        IFonteDaExecucaoDne fonteFactory,
        IConfiguration configuracao)
    {
        GeoImportadorPaisEstadoCidade topo = new(ctx, NullLogger<GeoImportadorPaisEstadoCidade>.Instance);
        GeoImportadorDistritoBairro distritoBairro = new(ctx);
        ImportadorDeLogradourosPorCopia logradouro = new(ctx, configuracao, Relogio, NullLogger<ImportadorDeLogradourosPorCopia>.Instance);
        GeoImportadorLocalidades folhas = new(distritoBairro, logradouro, NullLogger<GeoImportadorLocalidades>.Instance);
        return new ImportadorDaVersaoDne(ctx, topo, folhas, fonteFactory, Relogio);
    }

    private FinalizacaoDaCargaDne CriarFinalizacao(
        Lazy<IGeoCepCacheInvalidador> cacheLazy,
        GeoEtlMetrics metricas,
        IFonteDaExecucaoDne fonteDaExecucao)
    {
        ServiceCollection services = new();
        services.AddScoped(_ => CriarContexto());
        ServiceProvider provider = services.BuildServiceProvider();

        return new FinalizacaoDaCargaDne(
            provider.GetRequiredService<IServiceScopeFactory>(),
            cacheLazy,
            metricas,
            fonteDaExecucao,
            Relogio,
            NullLogger<FinalizacaoDaCargaDne>.Instance);
    }
}
