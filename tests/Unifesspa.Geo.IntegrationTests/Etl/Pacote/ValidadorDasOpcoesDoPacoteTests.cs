namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>Validação das opções do envio do pacote em <c>Geo:Etl</c>.</summary>
public sealed class ValidadorDasOpcoesDoPacoteTests
{
    private static readonly ValidadorDasOpcoesDoPacote Validador = new();

    [Fact(DisplayName = "Os padrões são válidos: 128 MiB, 32 entradas e um diretório informado")]
    public void Padroes_SaoValidos()
    {
        EtlOpcoes opcoes = new();

        opcoes.TamanhoMaximoDoPacote.Should().Be(128L * 1024 * 1024);
        opcoes.QuantidadeMaximaDeEntradasDoPacote.Should().Be(32);
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

    [Theory(DisplayName = "Quantidade máxima de entradas entre 16 e 256 é aceita")]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(256)]
    public void QuantidadeDeEntradasNaFaixa_EhAceita(int quantidade)
    {
        Validador.Validate(null, new EtlOpcoes { QuantidadeMaximaDeEntradasDoPacote = quantidade }).Succeeded.Should().BeTrue();
    }

    [Theory(DisplayName = "Quantidade máxima de entradas abaixo de 16 ou acima de 256 é recusada na partida")]
    [InlineData(15)]
    [InlineData(0)]
    [InlineData(257)]
    public void QuantidadeDeEntradasForaDaFaixa_EhRecusada(int quantidade)
    {
        ValidateOptionsResult resultado = Validador.Validate(null, new EtlOpcoes { QuantidadeMaximaDeEntradasDoPacote = quantidade });

        resultado.Failed.Should().BeTrue();
        resultado.FailureMessage.Should().Contain(nameof(EtlOpcoes.QuantidadeMaximaDeEntradasDoPacote)).And.Contain("entre 16 e 256");
    }

    [Fact(DisplayName = "As três faltas voltam juntas")]
    public void TresFaltas_VoltamJuntas()
    {
        ValidateOptionsResult resultado = Validador.Validate(
            null,
            new EtlOpcoes { TamanhoMaximoDoPacote = 0, DiretorioDoPacote = "", QuantidadeMaximaDeEntradasDoPacote = 1 });

        resultado.Failures.Should().HaveCount(3);
    }
}
