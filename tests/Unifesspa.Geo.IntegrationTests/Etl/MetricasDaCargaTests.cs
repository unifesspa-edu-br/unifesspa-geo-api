namespace Unifesspa.Geo.IntegrationTests.Etl;

using System.Diagnostics.Metrics;
using System.Text.Json;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Core.Observability;
using Unifesspa.Geo.Infrastructure.Observability;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

using static Unifesspa.Geo.IntegrationTests.Etl.CenarioDaCargaPeriodica;

/// <summary>
/// Métricas da carga sobre Postgres+PostGIS real: a carga concluída e a falhada emitem os
/// instrumentos geo.etl.* com o status e as linhas coerentes com o relatório.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class MetricasDaCargaTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDaCargaPeriodica _cenario;

    public MetricasDaCargaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDaCargaPeriodica(fixture);
    }

    [Fact(DisplayName = "#693: carga concluída emite geo.etl.* com status=concluida e linhas coerentes com o relatório")]
    public async Task Metricas_CargaConcluida_EmiteInstrumentos()
    {
        await _cenario.LimparAsync();
        Guid id = await _cenario.CriarExecucaoAsync("202601");
        FonteFactoryFake fonteFactory = new(new Dictionary<string, IGeoFonteDados>(StringComparer.Ordinal)
        {
            ["202601"] = FonteCompleta("202601"),
        });

        List<MedidaMetrica<long>> longs = [];
        List<MedidaMetrica<double>> doubles = [];

        await using GeoDbContext ctx = _cenario.CriarContexto();
        using GeoEtlMetrics metricas = new();
        using MeterListener listener = CriarListener(longs, doubles);
        listener.Start();

        Lazy<IGeoCepCacheInvalidador> cacheLazy = new(() => Substitute.For<IGeoCepCacheInvalidador>());
        CargaDaImportacao executor = _cenario.CriarExecutor(ctx, fonteFactory, cacheLazy, metricas);
        await executor.ExecutarAsync(await _cenario.ReservarAsync(id), CancellationToken.None);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id);
        execucao.Status.Should().Be(StatusImportacao.Concluida);

        RelatorioImportacaoDto relatorio = JsonSerializer.Deserialize<RelatorioImportacaoDto>(
            execucao.RelatorioJson!, RelatorioJsonCamelCase)!;
        long linhasEsperadas = relatorio.Inseridos + relatorio.Atualizados;

        List<MedidaMetrica<long>> execucoes = [.. longs.Where(m => m.Nome == "geo.etl.execucoes")];
        execucoes.Should().ContainSingle();
        execucoes[0].Valor.Should().Be(1);
        Status(execucoes[0].Tags).Should().Be("concluida");

        List<MedidaMetrica<long>> linhas = [.. longs.Where(m => m.Nome == "geo.etl.linhas")];
        linhas.Should().ContainSingle();
        linhas[0].Valor.Should().Be(linhasEsperadas, "as linhas reportadas batem com inseridos + atualizados do relatório");

        doubles.Should().Contain(m => m.Nome == "geo.etl.duracao" && Status(m.Tags) == "concluida");
    }

    [Fact(DisplayName = "#693: carga falhada emite geo.etl.execucoes/duracao com status=falhou (sem linhas)")]
    public async Task Metricas_CargaFalhada_EmiteStatusFalhou()
    {
        await _cenario.LimparAsync();
        Guid id = await _cenario.CriarExecucaoAsync("202601");
        FonteFactoryFake fonteFactory = new(new Dictionary<string, IGeoFonteDados>(StringComparer.Ordinal)
        {
            ["202601"] = new FonteComFalhaNasCidades(FonteCompleta("202601")),
        });

        List<MedidaMetrica<long>> longs = [];
        List<MedidaMetrica<double>> doubles = [];

        await using GeoDbContext ctx = _cenario.CriarContexto();
        using GeoEtlMetrics metricas = new();
        using MeterListener listener = CriarListener(longs, doubles);
        listener.Start();

        Lazy<IGeoCepCacheInvalidador> cacheLazy = new(() => Substitute.For<IGeoCepCacheInvalidador>());
        CargaDaImportacao executor = _cenario.CriarExecutor(ctx, fonteFactory, cacheLazy, metricas);
        await executor.ExecutarAsync(await _cenario.ReservarAsync(id), CancellationToken.None);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id);
        execucao.Status.Should().Be(StatusImportacao.Falhou);

        List<MedidaMetrica<long>> execucoes = [.. longs.Where(m => m.Nome == "geo.etl.execucoes")];
        execucoes.Should().ContainSingle();
        execucoes[0].Valor.Should().Be(1);
        Status(execucoes[0].Tags).Should().Be("falhou");

        doubles.Should().Contain(m => m.Nome == "geo.etl.duracao" && Status(m.Tags) == "falhou");
        longs.Should().NotContain(m => m.Nome == "geo.etl.linhas", "RegistrarFalha não contabiliza linhas");
    }

    // Escuta apenas os instrumentos geo.etl.* do Meter do serviço Geo, copiando as tags do span
    // (não capturável em lambda) para um array. xUnit serializa os testes da coleção, então só a
    // carga sob teste emite enquanto este listener está vivo.
    private static MeterListener CriarListener(List<MedidaMetrica<long>> longs, List<MedidaMetrica<double>> doubles)
    {
        MeterListener listener = new()
        {
            InstrumentPublished = (instrumento, l) =>
            {
                if (instrumento.Meter.Name == GeoServiceNames.Geo &&
                    instrumento.Name.StartsWith("geo.etl.", StringComparison.Ordinal))
                {
                    l.EnableMeasurementEvents(instrumento);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrumento, medida, tags, estado) =>
            longs.Add(new MedidaMetrica<long>(instrumento.Name, medida, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrumento, medida, tags, estado) =>
            doubles.Add(new MedidaMetrica<double>(instrumento.Name, medida, tags.ToArray())));
        return listener;
    }

    private static string? Status(KeyValuePair<string, object?>[] tags) =>
        tags.SingleOrDefault(t => t.Key == "status").Value as string;

    private readonly record struct MedidaMetrica<T>(string Nome, T Valor, KeyValuePair<string, object?>[] Tags);
}
