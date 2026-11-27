namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class FaixaDeCepTests
{
    [Fact(DisplayName = "Os limites da faixa são aparados")]
    public void Limites_SaoAparados()
    {
        FaixaDeCep faixa = new(" 66000000 ", "  68899999");

        faixa.CepInicial.Should().Be("66000000");
        faixa.CepFinal.Should().Be("68899999");
    }

    [Fact(DisplayName = "O limite em branco ou nulo fica nulo e não conta como informado")]
    public void Limite_EmBrancoOuNulo_FicaNulo()
    {
        FaixaDeCep faixa = new("   ", null);

        faixa.CepInicial.Should().BeNull();
        faixa.CepFinal.Should().BeNull();
        faixa.TemCepInicial.Should().BeFalse();
        faixa.TemCepFinal.Should().BeFalse();
    }

    [Fact(DisplayName = "Cada limite informado conta como informado sem depender do outro")]
    public void Limite_Informado_ContaComoInformado()
    {
        FaixaDeCep soInicial = new("66000000", null);
        FaixaDeCep soFinal = new(null, "68899999");

        (soInicial.TemCepInicial, soInicial.TemCepFinal).Should().Be((true, false));
        (soFinal.TemCepInicial, soFinal.TemCepFinal).Should().Be((false, true));
    }
}
