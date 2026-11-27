namespace Unifesspa.Geo.IntegrationTests.TestSupport.CatalogoDeErros;

using AwesomeAssertions;

public sealed class RegrasDoTextoDaDescricaoTests
{
    [Theory(DisplayName = "Texto com dado pessoal é apontado, inclusive em bloco de código")]
    [InlineData("O CPF 123.456.789-09 não existe", "CPF")]
    [InlineData("```\n12345678909\n```", "CPF")]
    [InlineData("Protocolo 123456789012345", "sequência de 11 ou mais dígitos")]
    [InlineData("Escreva para pessoa@exemplo.org", "e-mail")]
    public void TextoComDadoPessoal(string texto, string regra) =>
        RegrasDoTextoDaDescricao.Conferir(texto).Should().Contain(regra);

    [Fact(DisplayName = "Texto sem dado pessoal e com números curtos não gera problema")]
    public void TextoLimpo() =>
        RegrasDoTextoDaDescricao.Conferir("O CEP 68000-000 e o código 409 são exemplos públicos.").Should().BeEmpty();

    [Theory(DisplayName = "Requisito só vale no formato UNI-REQ-NNNN")]
    [InlineData("UNI-REQ-0530", true)]
    [InlineData("UNI-REQ-530", false)]
    [InlineData("RN12", false)]
    [InlineData("UNI-REQ-05300", false)]
    public void FormatoDoRequisito(string requisito, bool valido) =>
        RegrasDoTextoDaDescricao.Requisito().IsMatch(requisito).Should().Be(valido);

    [Theory(DisplayName = "Código público tem a forma uniplus.<segmentos em minúsculas>")]
    [InlineData("uniplus.geo.cep.nao_encontrado", true)]
    [InlineData("uniplus", false)]
    [InlineData("Uniplus.geo.x", false)]
    [InlineData("uniplus.geo.Cep", false)]
    public void FormaDoCodigo(string codigo, bool valido) =>
        RegrasDoTextoDaDescricao.CodigoPublico().IsMatch(codigo).Should().Be(valido);
}
