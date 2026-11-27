namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>Validação das opções do envio do pacote em <c>Geo:Etl</c>.</summary>
public sealed class ValidadorDasOpcoesDoPacoteTests
{
    private static readonly ValidadorDasOpcoesDoPacote Validador = new();

    [Fact(DisplayName = "Os padrões são válidos: 128 MiB e um diretório informado")]
    public void Padroes_SaoValidos()
    {
        EtlOpcoes opcoes = new();

        opcoes.TamanhoMaximoDoPacote.Should().Be(128L * 1024 * 1024);
        opcoes.DuracaoMaximaDaCarga.Should().Be(TimeSpan.FromHours(1));
        Validador.Validate(null, opcoes).Succeeded.Should().BeTrue();
    }

    [Theory(DisplayName = "Tamanho máximo menor ou igual a zero é recusado")]
    [InlineData(0)]
    [InlineData(-1)]
    public void TamanhoNaoPositivo_EhRecusado(long tamanho)
    {
        ValidateOptionsResult resultado = Validador.Validate(null, new EtlOpcoes { TamanhoMaximoDoPacote = tamanho });

        resultado.Failed.Should().BeTrue();
        resultado.FailureMessage.Should().Contain(nameof(EtlOpcoes.TamanhoMaximoDoPacote));
    }

    [Theory(DisplayName = "Diretório vazio ou em branco é recusado")]
    [InlineData("")]
    [InlineData("   ")]
    public void DiretorioEmBranco_EhRecusado(string diretorio)
    {
        ValidateOptionsResult resultado = Validador.Validate(null, new EtlOpcoes { DiretorioDoPacote = diretorio });

        resultado.Failed.Should().BeTrue();
        resultado.FailureMessage.Should().Contain(nameof(EtlOpcoes.DiretorioDoPacote));
    }

    [Fact(DisplayName = "As duas faltas voltam juntas")]
    public void DuasFaltas_VoltamJuntas()
    {
        ValidateOptionsResult resultado = Validador.Validate(null, new EtlOpcoes { TamanhoMaximoDoPacote = 0, DiretorioDoPacote = "" });

        resultado.Failures.Should().HaveCount(2);
    }
}
