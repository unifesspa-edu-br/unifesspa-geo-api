namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class NomeComFormaDeBuscaTests
{
    [Fact(DisplayName = "O nome é aparado e mantém a caixa original")]
    public void Nome_EAparadoEMantemACaixa()
    {
        NomeComFormaDeBusca nome = new("  Marabá D'Oeste ", null);

        nome.Nome.Should().Be("Marabá D'Oeste");
    }

    [Fact(DisplayName = "O nome nulo é recusado")]
    public void Nome_NuloERecusado()
    {
        Action criar = () => _ = new NomeComFormaDeBusca(null!, "maraba");

        criar.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "A forma de busca é aparada e vai em minúsculas")]
    public void FormaDeBusca_EAparadaEEmMinusculas()
    {
        NomeComFormaDeBusca nome = new("Marabá", "  MARABA ");

        nome.NomeNormalizado.Should().Be("maraba");
    }

    [Fact(DisplayName = "A forma de busca em branco ou nula fica nula")]
    public void FormaDeBusca_EmBrancoOuNula_FicaNula()
    {
        new NomeComFormaDeBusca("Marabá", "   ").NomeNormalizado.Should().BeNull();
        new NomeComFormaDeBusca("Marabá", null).NomeNormalizado.Should().BeNull();
    }
}
