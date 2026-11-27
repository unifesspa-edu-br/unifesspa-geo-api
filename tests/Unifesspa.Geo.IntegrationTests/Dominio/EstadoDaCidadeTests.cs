namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class EstadoDaCidadeTests
{
    [Fact(DisplayName = "A UF é aparada e fica em maiúsculas")]
    public void Uf_EAparadaEEmMaiusculas()
    {
        EstadoDaCidade estado = new(Guid.NewGuid(), " pa ");

        estado.Uf.Should().Be("PA");
    }

    [Fact(DisplayName = "A UF nula é recusada")]
    public void Uf_NulaERecusada()
    {
        Action criar = () => _ = new EstadoDaCidade(Guid.NewGuid(), null!);

        criar.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "O identificador do estado é mantido")]
    public void EstadoId_EMantido()
    {
        Guid estadoId = Guid.NewGuid();

        new EstadoDaCidade(estadoId, "PA").EstadoId.Should().Be(estadoId);
    }
}
