namespace Unifesspa.Geo.IntegrationTests.Etl;

using System.Text.Json;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Caching;
using Unifesspa.Geo.Infrastructure.Cep;
using Unifesspa.Geo.Infrastructure.Core.Caching;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

using static Unifesspa.Geo.IntegrationTests.Etl.CenarioDaCargaPeriodica;

/// <summary>
/// Conclusão da carga sobre Postgres+PostGIS real: a execução registra status, relatório por
/// tabela e disparador, sem dado pessoal; a recarga troca o selo do cache de CEP; falha,
/// cancelamento ou cache indisponível não deixam a execução em andamento nem selam o cache.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class SeloDoCacheEConclusaoDaCargaTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDaCargaPeriodica _cenario;

    public SeloDoCacheEConclusaoDaCargaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDaCargaPeriodica(fixture);
    }

    [Fact(DisplayName = "Recarga da MESMA versão troca o selo do cache: resposta cacheada antes da recarga deixa de ser servida")]
    public async Task RecargaMesmaVersao_InvalidaEntradasCacheadas()
    {
        await _cenario.LimparAsync();
        MemoriaCacheFake cache = new();
        IGeoCepCacheInvalidador invalidador = new RedisGeoCepCacheInvalidador(cache, NullLogger<RedisGeoCepCacheInvalidador>.Instance);
        FonteFactoryFake fonteFactory = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal)
        {
            ["202601"] = FonteCompleta("202601"),
        });
        ICepReader reader = Substitute.For<ICepReader>();
        reader.ResolverAsync(CepMaraba, Arg.Any<CancellationToken>()).Returns(
            new CepResolvidoDto(CepMaraba, null, null, null, null, null, "Marabá", Maraba, "PA", null, null, CepResolucao.NivelCidade, CepResolucao.OrigemFaixaCidade));
        using Microsoft.Extensions.Caching.Memory.MemoryCache memoria = new(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        CepResolver resolver = new(
            new Lazy<ICacheService>(() => cache), reader, memoria, _cenario.Relogio,
            Microsoft.Extensions.Options.Options.Create(new GeoCepCacheOptions { SeloTtl = TimeSpan.Zero }),
            NullLogger<CepResolver>.Instance);

        await _cenario.ExecutarAsync(await _cenario.CriarExecucaoAsync("202601"), fonteFactory, invalidador);
        await resolver.ResolverAsync(CepMaraba, CancellationToken.None);
        await resolver.ResolverAsync(CepMaraba, CancellationToken.None);
        await reader.Received(1).ResolverAsync(CepMaraba, Arg.Any<CancellationToken>());

        await _cenario.ExecutarAsync(await _cenario.CriarExecucaoAsync("202601"), fonteFactory, invalidador);
        await resolver.ResolverAsync(CepMaraba, CancellationToken.None);

        await reader.Received(2).ResolverAsync(CepMaraba, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "CA-07/08: a carga conclui registrando status, relatório por tabela (contadores) e disparador — sem PII")]
    public async Task Carga_RegistraRelatorio_SemPii()
    {
        await _cenario.LimparAsync();
        IGeoCepCacheInvalidador cache = Substitute.For<IGeoCepCacheInvalidador>();
        FonteFactoryFake fonteFactory = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal)
        {
            ["202601"] = FonteCompleta("202601"),
        });

        Guid id = await _cenario.CriarExecucaoAsync("202601");
        await _cenario.ExecutarAsync(id, fonteFactory, cache);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id);

        execucao.Status.Should().Be(StatusImportacao.Concluida);
        execucao.ConcluidoEm.Should().NotBeNull();
        execucao.DisparadoPor.Should().Be("teste");
        execucao.RelatorioJson.Should().NotBeNullOrWhiteSpace();

        // O relatorio_json é persistido em camelCase (alinhado ao wire da API); lê no mesmo formato.
        RelatorioImportacaoDto? relatorio = JsonSerializer.Deserialize<RelatorioImportacaoDto>(
            execucao.RelatorioJson!, RelatorioJsonCamelCase);
        relatorio.Should().NotBeNull();
        relatorio!.VersaoDataset.Should().Be("202601");
        relatorio.Tabelas.Should().Contain(t => t.Tabela == "cidade" && t.Inseridos >= 2);
        relatorio.Inseridos.Should().BeGreaterThan(0);
    }

    [Fact(DisplayName = "Robustez: falha na carga marca a execução como Falhou e NÃO sela o cache (a versão vigente não mudou)")]
    public async Task Falha_MarcaFalhou_E_NaoSelaCache()
    {
        await _cenario.LimparAsync();
        IGeoCepCacheInvalidador cache = Substitute.For<IGeoCepCacheInvalidador>();
        FonteFactoryFake fonteFactory = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal)
        {
            ["202601"] = new FonteComFalhaNasCidades(FonteCompleta("202601")),
        });

        Guid id = await _cenario.CriarExecucaoAsync("202601");
        await _cenario.ExecutarAsync(id, fonteFactory, cache);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id);

        execucao.Status.Should().Be(StatusImportacao.Falhou);
        execucao.ConcluidoEm.Should().NotBeNull();
        await cache.DidNotReceive().InvalidarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Robustez: cancelamento/desligamento durante a carga marca a execução Falhou (libera o índice) e não sela o cache")]
    public async Task Cancelamento_MarcaFalhou_E_NaoSelaCache()
    {
        await _cenario.LimparAsync();
        IGeoCepCacheInvalidador cache = Substitute.For<IGeoCepCacheInvalidador>();
        FonteFactoryCancelada fonteFactory = new();

        Guid id = await _cenario.CriarExecucaoAsync("202601");
        Func<Task> acao = () => _cenario.ExecutarAsync(id, fonteFactory, cache);

        await acao.Should().ThrowAsync<OperationCanceledException>();

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id);
        execucao.Status.Should().Be(StatusImportacao.Falhou, "a instância dona marca terminal já, não deixa EmAndamento bloqueando");
        await cache.DidNotReceive().InvalidarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Robustez: cache indisponível ao selar (ex.: Redis fora resolvido pelo Lazy) não reverte a carga já concluída")]
    public async Task SeloComCacheIndisponivel_NaoFalhaCarga()
    {
        await _cenario.LimparAsync();
        FonteFactoryFake fonteFactory = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal)
        {
            ["202601"] = FonteCompleta("202601"),
        });
        // Lazy que lança ao resolver — simula o IConnectionMultiplexer.Connect falhando no .Value.
        Lazy<IGeoCepCacheInvalidador> cacheQuebrado = new(() => throw new InvalidOperationException("Redis fora do ar"));

        Guid id = await _cenario.CriarExecucaoAsync("202601");
        Func<Task> acao = () => _cenario.ExecutarComLazyAsync(id, fonteFactory, cacheQuebrado);

        await acao.Should().NotThrowAsync();

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id);
        execucao.Status.Should().Be(StatusImportacao.Concluida, "falha de cache ao selar é best-effort, não pode reverter a carga");
    }
}
