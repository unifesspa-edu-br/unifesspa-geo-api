namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class UfNoPaisTests
{
    [Fact(DisplayName = "A sigla da UF é aparada e fica em maiúsculas")]
    public void Sigla_EAparadaEEmMaiusculas()
    {
        UfNoPais uf = new(Guid.NewGuid(), " sp ");

        uf.Sigla.Should().Be("SP");
    }

    [Fact(DisplayName = "A sigla nula é recusada")]
    public void Sigla_NulaERecusada()
    {
        Action criar = () => _ = new UfNoPais(Guid.NewGuid(), null!);

        criar.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "O identificador do país é mantido")]
    public void PaisId_EMantido()
    {
        Guid paisId = Guid.NewGuid();

        new UfNoPais(paisId, "SP").PaisId.Should().Be(paisId);
    }
}
