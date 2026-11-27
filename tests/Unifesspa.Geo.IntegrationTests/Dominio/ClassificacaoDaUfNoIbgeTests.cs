namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class ClassificacaoDaUfNoIbgeTests
{
    [Fact(DisplayName = "O código do IBGE e a região são aparados")]
    public void CodigoERegiao_SaoAparados()
    {
        ClassificacaoDaUfNoIbge classificacao = new(" 15 ", "  Norte ");

        classificacao.CodigoIbge.Should().Be("15");
        classificacao.Regiao.Should().Be("Norte");
    }

    [Fact(DisplayName = "O código do IBGE e a região em branco ou nulos ficam nulos")]
    public void CodigoERegiao_EmBrancoOuNulos_FicamNulos()
    {
        ClassificacaoDaUfNoIbge classificacao = new("   ", null);

        classificacao.CodigoIbge.Should().BeNull();
        classificacao.Regiao.Should().BeNull();
    }
}
