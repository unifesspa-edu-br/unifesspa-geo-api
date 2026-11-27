namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class DivisaoRegionalDaCidadeTests
{
    [Fact(DisplayName = "A divisão guarda cada uma das quatro regiões, já aparadas")]
    public void Divisao_GuardaAsQuatroRegioes()
    {
        DivisaoRegionalDaCidade divisao = new(
            new RegiaoGeografica(" 1506 ", " Sudeste Paraense "),
            new RegiaoGeografica("15021", "Marabá"),
            new RegiaoGeografica("1502", "  "),
            new RegiaoGeografica(null, null));

        divisao.Mesorregiao.Should().Be(new RegiaoGeografica("1506", "Sudeste Paraense"));
        divisao.Microrregiao.Should().Be(new RegiaoGeografica("15021", "Marabá"));
        divisao.RegiaoIntermediaria.Should().Be(new RegiaoGeografica("1502", null));
        divisao.RegiaoImediata.Should().Be(new RegiaoGeografica(null, null));
    }
}
