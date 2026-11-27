namespace Unifesspa.Geo.IntegrationTests.Registros;

using AwesomeAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Core.Middleware;

/// <summary>
/// Na consulta da requisição, o valor dos parâmetros sensíveis sai como <c>***</c>; o resto da consulta,
/// a ordem dos parâmetros e a forma como foram escritos permanecem.
/// </summary>
public sealed class ConsultaMascaradaNoRegistroTests
{
    [Theory(DisplayName = "Parâmetro sensível padrão tem o valor mascarado")]
    [InlineData("cpf")]
    [InlineData("email")]
    [InlineData("senha")]
    [InlineData("password")]
    [InlineData("token")]
    public void ParametroSensivelPadraoTemOValorMascarado(string nome)
    {
        Mascarar($"?{nome}=valor&pagina=2").Should().Be($"?{nome}=***&pagina=2");
    }

    [Fact(DisplayName = "O nome do parâmetro sensível ignora maiúsculas e minúsculas")]
    public void NomeDoParametroSensivelIgnoraCaixa()
    {
        Mascarar("?CpF=1&Token=2").Should().Be("?CpF=***&Token=***");
    }

    [Fact(DisplayName = "Nome codificado com percentual é comparado decodificado e mantido como veio")]
    public void NomeCodificadoEComparadoDecodificado()
    {
        Mascarar("?%63%70%66=123&x=1").Should().Be("?%63%70%66=***&x=1");
    }

    [Fact(DisplayName = "Parâmetro comum mantém o valor exatamente como foi escrito")]
    public void ParametroComumMantemOValorComoFoiEscrito()
    {
        Mascarar("?q=a%20b+c&n=1").Should().Be("?q=a%20b+c&n=1");
    }

    [Fact(DisplayName = "Pedaços vazios entre os parâmetros são descartados")]
    public void PedacosVaziosSaoDescartados()
    {
        Mascarar("?&a=1&&b=2&").Should().Be("?a=1&b=2");
    }

    [Fact(DisplayName = "Parâmetro sem sinal de igual é mantido sem alteração, mesmo com nome sensível")]
    public void ParametroSemIgualEMantido()
    {
        Mascarar("?avulso&cpf&x=1").Should().Be("?avulso&cpf&x=1");
    }

    [Fact(DisplayName = "Parâmetro sensível com valor vazio recebe a máscara")]
    public void ParametroSensivelComValorVazioRecebeAMascara()
    {
        Mascarar("?cpf=").Should().Be("?cpf=***");
    }

    [Fact(DisplayName = "Só o primeiro igual separa nome e valor")]
    public void SoOPrimeiroIgualSeparaNomeEValor()
    {
        Mascarar("?token=a=b&q=c=d").Should().Be("?token=***&q=c=d");
    }

    [Fact(DisplayName = "A ordem dos parâmetros é preservada")]
    public void AOrdemDosParametrosEPreservada()
    {
        Mascarar("?z=1&cpf=2&a=3&m=4").Should().Be("?z=1&cpf=***&a=3&m=4");
    }

    [Fact(DisplayName = "Só o ponto de interrogação volta como veio")]
    public void SoOPontoDeInterrogacaoVoltaComoVeio()
    {
        Mascarar("?").Should().Be("?");
    }

    [Fact(DisplayName = "Consulta ausente vira texto vazio")]
    public void ConsultaAusenteViraTextoVazio()
    {
        new QueryStringMasker(Options.Create(new RequestLoggingOptions()))
            .Mascarar(QueryString.Empty)
            .Should().BeEmpty();
    }

    [Fact(DisplayName = "Consulta só com separadores vira só o ponto de interrogação")]
    public void ConsultaSoComSeparadoresViraSoOPontoDeInterrogacao()
    {
        Mascarar("?&&").Should().Be("?");
    }

    [Fact(DisplayName = "Nomes e valor de máscara configurados substituem os padrões na comparação")]
    public void NomesEValorDeMascaraConfiguradosSaoUsados()
    {
        RequestLoggingOptions opcoes = new() { ValorMascarado = "[oculto]" };
        opcoes.NomesParametrosSensiveis.Add("documento");

        string resultado = new QueryStringMasker(Options.Create(opcoes))
            .Mascarar(new QueryString("?documento=1&cpf=2"));

        resultado.Should().Be("?documento=[oculto]&cpf=[oculto]");
    }

    private static string Mascarar(string consulta) =>
        new QueryStringMasker(Options.Create(new RequestLoggingOptions()))
            .Mascarar(new QueryString(consulta));
}
