namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class LocalidadeDoLogradouroTests
{
    [Fact(DisplayName = "A UF é aparada e fica em maiúsculas")]
    public void Uf_EAparadaEEmMaiusculas()
    {
        LocalidadeDoLogradouro localidade = new(Guid.NewGuid(), null, null, " pa ");

        localidade.Uf.Should().Be("PA");
    }

    [Fact(DisplayName = "A UF nula é recusada")]
    public void Uf_NulaERecusada()
    {
        Action criar = () => _ = new LocalidadeDoLogradouro(Guid.NewGuid(), null, null, null!);

        criar.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "A cidade é mantida e o distrito e o bairro são opcionais")]
    public void Identificadores_SaoMantidos()
    {
        Guid cidadeId = Guid.NewGuid();
        Guid bairroId = Guid.NewGuid();

        LocalidadeDoLogradouro localidade = new(cidadeId, null, bairroId, "PA");

        (localidade.CidadeId, localidade.DistritoId, localidade.BairroId).Should().Be((cidadeId, (Guid?)null, (Guid?)bairroId));
    }
}
