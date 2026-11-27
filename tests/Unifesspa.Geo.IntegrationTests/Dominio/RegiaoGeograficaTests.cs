namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class RegiaoGeograficaTests
{
    [Fact(DisplayName = "O código e o nome da região são aparados")]
    public void CodigoENome_SaoAparados()
    {
        RegiaoGeografica regiao = new(" 1506 ", "  Sudeste Paraense ");

        regiao.Codigo.Should().Be("1506");
        regiao.Nome.Should().Be("Sudeste Paraense");
    }

    [Fact(DisplayName = "O código e o nome em branco ou nulos ficam nulos")]
    public void CodigoENome_EmBrancoOuNulos_FicamNulos()
    {
        RegiaoGeografica regiao = new("   ", null);

        regiao.Codigo.Should().BeNull();
        regiao.Nome.Should().BeNull();
    }
}
