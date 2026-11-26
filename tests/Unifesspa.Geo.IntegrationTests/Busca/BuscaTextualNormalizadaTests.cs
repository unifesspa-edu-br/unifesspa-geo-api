namespace Unifesspa.Geo.IntegrationTests.Busca;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence;

/// <summary>
/// Normalização do texto de busca. A regra comum (bairro, distrito, logradouro, grande
/// usuário e complemento) segue o texto sem acento da DNE e conserva o apóstrofo; a regra
/// do município também o retira, porque o nome do município é gravado sem apóstrofo.
/// </summary>
public sealed class BuscaTextualNormalizadaTests
{
    [Theory(DisplayName = "Regra comum: tira acento e caixa e conserva apóstrofo e variantes")]
    [InlineData("Caixa D´Água", "caixa d´agua")]
    [InlineData("Villa D`Oeste", "villa d`oeste")]
    [InlineData("Pau D'Arco", "pau d'arco")]
    [InlineData("PAU D’ARCO", "pau d’arco")]
    public void Normalizar_ConservaApostrofoEVariantes(string termo, string esperado)
    {
        BuscaTextualNormalizada.Normalizar(termo).Should().Be(esperado);
    }

    [Theory(DisplayName = "Regra do município: tira acento, caixa e todas as variantes de apóstrofo")]
    [InlineData("Pau D'Arco", "pau darco")]
    [InlineData("PAU D’ARCO", "pau darco")]
    [InlineData("Pau D‘Arco", "pau darco")]
    [InlineData("Pau D´Arco", "pau darco")]
    [InlineData("Pau D`Arco", "pau darco")]
    [InlineData("Pau DʼArco", "pau darco")]
    [InlineData("Pau DArco", "pau darco")]
    [InlineData("Sant'Ana do Livramento", "santana do livramento")]
    [InlineData("Mãe d'Água", "mae dagua")]
    public void NormalizarNomeDoMunicipio_RetiraApostrofoEVariantes(string nome, string esperado)
    {
        BuscaTextualNormalizada.NormalizarNomeDoMunicipio(nome).Should().Be(esperado);
    }

    [Fact(DisplayName = "As duas regras divergem só no apóstrofo: a comum mantém ´ e `, a do município os retira")]
    public void RegraComumEDoMunicipio_DivergemSoNoApostrofo()
    {
        const string termo = "Olhos D´Água e D`Oeste";

        BuscaTextualNormalizada.Normalizar(termo).Should().Be("olhos d´agua e d`oeste");
        BuscaTextualNormalizada.NormalizarNomeDoMunicipio(termo).Should().Be("olhos dagua e doeste");
    }

    [Fact(DisplayName = "Padrão do município monta %termo% sem apóstrofo; o comum o conserva")]
    public void CriarPadrao_MunicipioSemApostrofo_ComumComApostrofo()
    {
        BuscaTextualNormalizada.CriarPadraoContemNomeDoMunicipio("d'Oeste").Should().Be("%doeste%");
        BuscaTextualNormalizada.CriarPadraoContem("d´Agua").Should().Be("%d´agua%");
    }

    [Fact(DisplayName = "Padrão do município escapa os curingas do LIKE como texto literal")]
    public void CriarPadraoContemNomeDoMunicipio_EscapaCuringas()
    {
        BuscaTextualNormalizada.CriarPadraoContemNomeDoMunicipio("a_b%c\\d").Should().Be("%a\\_b\\%c\\\\d%");
    }

    [Theory(DisplayName = "Termo vazio ou em branco é recusado pelas duas regras")]
    [InlineData("")]
    [InlineData("   ")]
    public void TermoEmBranco_ELancadoPelasDuasRegras(string termo)
    {
        Action municipio = () => BuscaTextualNormalizada.NormalizarNomeDoMunicipio(termo);
        Action padrao = () => BuscaTextualNormalizada.CriarPadraoContemNomeDoMunicipio(termo);

        municipio.Should().Throw<ArgumentException>();
        padrao.Should().Throw<ArgumentException>();
    }
}
