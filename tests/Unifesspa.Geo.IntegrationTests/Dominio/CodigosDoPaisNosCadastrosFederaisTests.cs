namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class CodigosDoPaisNosCadastrosFederaisTests
{
    [Fact(DisplayName = "Cada código é aparado e guardado no cadastro a que pertence")]
    public void Codigos_SaoAparadosECadaUmNoSeuCadastro()
    {
        CodigosDoPaisNosCadastrosFederais codigos = new(" 1058 ", " 105 ", " 0105 ", "  1 ");

        codigos.CodigoBcb.Should().Be("1058");
        codigos.CodigoRfb.Should().Be("105");
        codigos.CodigoSped.Should().Be("0105");
        codigos.CodigoSiscomex.Should().Be("1");
    }

    [Fact(DisplayName = "O código em branco ou nulo fica nulo")]
    public void Codigo_EmBrancoOuNulo_FicaNulo()
    {
        CodigosDoPaisNosCadastrosFederais codigos = new("   ", null, "", " ");

        codigos.CodigoBcb.Should().BeNull();
        codigos.CodigoRfb.Should().BeNull();
        codigos.CodigoSped.Should().BeNull();
        codigos.CodigoSiscomex.Should().BeNull();
    }
}
