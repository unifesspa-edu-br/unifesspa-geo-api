namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class NomeDoLogradouroTests
{
    [Fact(DisplayName = "O tipo, o nome e o nome completo são aparados e mantêm a caixa original")]
    public void Textos_SaoAparadosEMantemACaixa()
    {
        NomeDoLogradouro nome = new(" Rua ", "  Presidente Vargas ", " Rua Presidente Vargas ", null);

        nome.Tipo.Should().Be("Rua");
        nome.Nome.Should().Be("Presidente Vargas");
        nome.NomeCompleto.Should().Be("Rua Presidente Vargas");
    }

    [Fact(DisplayName = "O nome nulo é recusado")]
    public void Nome_NuloERecusado()
    {
        Action criar = () => _ = new NomeDoLogradouro("Rua", null!, null, "rua a");

        criar.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "O tipo e o nome completo em branco ou nulos ficam nulos")]
    public void TipoENomeCompleto_EmBrancoOuNulos_FicamNulos()
    {
        NomeDoLogradouro nome = new("   ", "A", null, "a");

        nome.Tipo.Should().BeNull();
        nome.NomeCompleto.Should().BeNull();
    }

    [Fact(DisplayName = "A forma de busca é aparada e vai em minúsculas")]
    public void FormaDeBusca_EAparadaEEmMinusculas()
    {
        NomeDoLogradouro nome = new("Rua", "A", "Rua A", "  RUA A ");

        nome.NomeNormalizado.Should().Be("rua a");
    }

    [Fact(DisplayName = "A forma de busca em branco ou nula fica nula")]
    public void FormaDeBusca_EmBrancoOuNula_FicaNula()
    {
        new NomeDoLogradouro("Rua", "A", "Rua A", "  ").NomeNormalizado.Should().BeNull();
        new NomeDoLogradouro("Rua", "A", "Rua A", null).NomeNormalizado.Should().BeNull();
    }
}
