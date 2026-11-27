namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class ProvenienciaDaCargaTests
{
    [Fact(DisplayName = "A versão da base dos Correios é aparada")]
    public void Versao_EAparada()
    {
        ProvenienciaDaCarga proveniencia = new("  202601 ");

        proveniencia.VersaoDaBaseDosCorreios.Should().Be("202601");
    }

    [Fact(DisplayName = "A versão nula é recusada")]
    public void Versao_NulaERecusada()
    {
        Action criar = () => _ = new ProvenienciaDaCarga(null!);

        criar.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "A versão em branco é aceita pelo tipo mas não conta como versão informada")]
    public void Versao_EmBranco_NaoTemVersao()
    {
        ProvenienciaDaCarga proveniencia = new("   ");

        proveniencia.VersaoDaBaseDosCorreios.Should().BeEmpty();
        proveniencia.TemVersao.Should().BeFalse();
    }

    [Fact(DisplayName = "A versão com conteúdo conta como versão informada")]
    public void Versao_ComConteudo_TemVersao()
    {
        new ProvenienciaDaCarga("202601").TemVersao.Should().BeTrue();
    }

    [Fact(DisplayName = "O registro é vigente por padrão e a não vigência é mantida quando informada")]
    public void Vigencia_ComecaVigenteEMantemANaoVigencia()
    {
        new ProvenienciaDaCarga("202601").Vigente.Should().BeTrue();
        new ProvenienciaDaCarga("202601", Vigente: false).Vigente.Should().BeFalse();
    }

    [Fact(DisplayName = "Duas proveniências com a mesma versão aparada e a mesma vigência são iguais")]
    public void Igualdade_ConsideraAVersaoAparada()
    {
        new ProvenienciaDaCarga(" 202601 ").Should().Be(new ProvenienciaDaCarga("202601"));
    }
}
