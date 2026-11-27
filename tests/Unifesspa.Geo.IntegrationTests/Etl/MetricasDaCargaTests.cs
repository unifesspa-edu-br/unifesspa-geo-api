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
    private readonly CenarioDaCargaPeriodica _cenario;

    public MetricasDaCargaTests(GeoPostgisFixture fixture)
    {
        _cenario = new CenarioDaCargaPeriodica(fixture);
    }

    [Fact(DisplayName = "#693: carga concluída emite geo.etl.* com status=concluida e linhas coerentes com o relatório")]
    public async Task Metricas_CargaConcluida_EmiteInstrumentos()
    {
        await _cenario.LimparAsync();
        Guid id = await _cenario.CriarExecucaoAsync("202601");
        FabricaDeFonteFixa fonteFactory = new(FonteCompleta("202601"));
        using CapturaDasMetricasDaCarga captura = new();

        await ExecutarCargaAsync(id, fonteFactory);

        GeoImportacaoExecucao execucao = await _cenario.LerExecucaoAsync(id);
        execucao.Status.Should().Be(StatusImportacao.Concluida);
        ConferirUmaExecucaoComStatus(captura, "concluida");
        ConferirLinhasReportadas(captura, execucao);

        captura.Decimais.Should().Contain(m => m.Nome == "geo.etl.duracao" && CapturaDasMetricasDaCarga.Status(m.Tags) == "concluida");
    }

    [Fact(DisplayName = "#693: carga falhada emite geo.etl.execucoes/duracao com status=falhou (sem linhas)")]
    public async Task Metricas_CargaFalhada_EmiteStatusFalhou()
    {
        await _cenario.LimparAsync();
        Guid id = await _cenario.CriarExecucaoAsync("202601");
        FabricaDeFonteFixa fonteFactory = new(new FonteComFalhaNasCidades(FonteCompleta("202601")));
        using CapturaDasMetricasDaCarga captura = new();

        await ExecutarCargaAsync(id, fonteFactory);

        GeoImportacaoExecucao execucao = await _cenario.LerExecucaoAsync(id);
        execucao.Status.Should().Be(StatusImportacao.Falhou);
        ConferirUmaExecucaoComStatus(captura, "falhou");

        captura.Decimais.Should().Contain(m => m.Nome == "geo.etl.duracao" && CapturaDasMetricasDaCarga.Status(m.Tags) == "falhou");
        captura.Inteiras.Should().NotContain(m => m.Nome == "geo.etl.linhas", "RegistrarFalha não contabiliza linhas");
    }

    private static void ConferirUmaExecucaoComStatus(CapturaDasMetricasDaCarga captura, string status)
    {
        List<MedidaMetrica<long>> execucoes = captura.InteirasDe("geo.etl.execucoes");
        execucoes.Should().ContainSingle();
        execucoes[0].Valor.Should().Be(1);
        CapturaDasMetricasDaCarga.Status(execucoes[0].Tags).Should().Be(status);
    }

    private static void ConferirLinhasReportadas(CapturaDasMetricasDaCarga captura, GeoImportacaoExecucao execucao)
    {
        RelatorioImportacaoDto relatorio = JsonSerializer.Deserialize<RelatorioImportacaoDto>(
            execucao.RelatorioJson!, RelatorioJsonCamelCase)!;
        long linhasEsperadas = relatorio.Inseridos + relatorio.Atualizados;

        List<MedidaMetrica<long>> linhas = captura.InteirasDe("geo.etl.linhas");
        linhas.Should().ContainSingle();
        linhas[0].Valor.Should().Be(linhasEsperadas, "as linhas reportadas batem com inseridos + atualizados do relatório");
    }

    private async Task ExecutarCargaAsync(Guid id, FabricaDeFonteFixa fonteFactory)
    {
        await using GeoDbContext ctx = _cenario.CriarContexto();
        using GeoEtlMetrics metricas = new();
        Lazy<IGeoCepCacheInvalidador> cacheLazy = new(() => Substitute.For<IGeoCepCacheInvalidador>());
        CargaDaImportacao executor = _cenario.CriarExecutor(ctx, fonteFactory, cacheLazy, metricas);
        await executor.ExecutarAsync(await _cenario.ReservarAsync(id), CancellationToken.None);
    }
}
