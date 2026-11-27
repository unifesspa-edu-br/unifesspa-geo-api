namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class MetricasDoEstadoTests
{
    [Fact(DisplayName = "Cada métrica é guardada na propriedade de mesmo nome, sem trocar uma pela outra")]
    public void Metricas_SaoGuardadasPeloNome()
    {
        MetricasDoEstado metricas = Preenchidas();

        ConferirOsDecimais(metricas);
        ConferirOsInteiros(metricas);
    }

    [Fact(DisplayName = "Métrica não informada fica nula")]
    public void Metrica_NaoInformada_FicaNula()
    {
        MetricasDoEstado metricas = new() { Idh = 0.5m };

        metricas.AreaKm2.Should().BeNull();
        metricas.PopulacaoResidente2022.Should().BeNull();
        metricas.TotalVeiculos2023.Should().BeNull();
    }

    private static MetricasDoEstado Preenchidas() =>
        new()
        {
            AreaKm2 = 1.5m,
            PopulacaoResidente2022 = 2,
            DensidadeDemografica = 3.5m,
            MatriculasEnsinoFundamental2023 = 4,
            Idh = 0.5m,
            ReceitasBrutas = 6.5m,
            DespesasBrutas = 7.5m,
            RendimentoMensalPerCapita = 8,
            TotalVeiculos2023 = 9,
        };

    private static void ConferirOsDecimais(MetricasDoEstado metricas)
    {
        metricas.AreaKm2.Should().Be(1.5m);
        metricas.DensidadeDemografica.Should().Be(3.5m);
        metricas.Idh.Should().Be(0.5m);
        metricas.ReceitasBrutas.Should().Be(6.5m);
        metricas.DespesasBrutas.Should().Be(7.5m);
    }

    private static void ConferirOsInteiros(MetricasDoEstado metricas)
    {
        metricas.PopulacaoResidente2022.Should().Be(2);
        metricas.MatriculasEnsinoFundamental2023.Should().Be(4);
        metricas.RendimentoMensalPerCapita.Should().Be(8);
        metricas.TotalVeiculos2023.Should().Be(9);
    }
}
