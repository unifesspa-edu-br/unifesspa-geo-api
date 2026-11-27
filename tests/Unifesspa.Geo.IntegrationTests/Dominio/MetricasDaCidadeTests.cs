namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class MetricasDaCidadeTests
{
    [Fact(DisplayName = "Cada métrica é guardada na propriedade de mesmo nome, sem trocar uma pela outra")]
    public void Metricas_SaoGuardadasPeloNome()
    {
        MetricasDaCidade metricas = Preenchidas();

        ConferirOsDecimais(metricas);
        metricas.PopulacaoResidente.Should().Be(2);
    }

    [Fact(DisplayName = "Métrica não informada fica nula")]
    public void Metrica_NaoInformada_FicaNula()
    {
        MetricasDaCidade metricas = new() { Idh = 0.5m };

        metricas.AreaKm2.Should().BeNull();
        metricas.PopulacaoResidente.Should().BeNull();
        metricas.PibPerCapita.Should().BeNull();
    }

    private static MetricasDaCidade Preenchidas() =>
        new()
        {
            AreaKm2 = 1.5m,
            PopulacaoResidente = 2,
            DensidadeDemografica = 3.5m,
            Escolarizacao6a14 = 4.5m,
            Idh = 0.5m,
            MortalidadeInfantil = 6.5m,
            Receitas = 7.5m,
            Despesas = 8.5m,
            PibPerCapita = 9.5m,
        };

    private static void ConferirOsDecimais(MetricasDaCidade metricas)
    {
        metricas.AreaKm2.Should().Be(1.5m);
        metricas.DensidadeDemografica.Should().Be(3.5m);
        metricas.Escolarizacao6a14.Should().Be(4.5m);
        metricas.Idh.Should().Be(0.5m);
        metricas.MortalidadeInfantil.Should().Be(6.5m);
        metricas.Receitas.Should().Be(7.5m);
        metricas.Despesas.Should().Be(8.5m);
        metricas.PibPerCapita.Should().Be(9.5m);
    }
}
