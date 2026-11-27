namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>Texto e ordem das faltas que a validação das opções do envio do pacote devolve.</summary>
public sealed class FaltasDasOpcoesDoPacoteTests
{
    private static readonly ValidadorDasOpcoesDoPacote Validador = new();

    [Fact(DisplayName = "As três faltas voltam juntas, na ordem tamanho, entradas e diretório, com o texto exato")]
    public void TresFaltas_VoltamNaOrdemComOTextoExato()
    {
        EtlOpcoes opcoes = new() { TamanhoMaximoDoPacote = 0, QuantidadeMaximaDeEntradasDoPacote = 1, DiretorioDoPacote = " " };

        ValidateOptionsResult resultado = Validador.Validate(null, opcoes);

        resultado.Failures.Should().Equal(
            "Geo:Etl:TamanhoMaximoDoPacote deve ser maior que zero.",
            "Geo:Etl:QuantidadeMaximaDeEntradasDoPacote deve estar entre 16 e 256.",
            "Geo:Etl:DiretorioDoPacote deve ser informado.");
    }

    [Fact(DisplayName = "Só o tamanho inválido devolve só a falta do tamanho")]
    public void SoOTamanho_DevolveSoAFaltaDoTamanho()
    {
        ValidateOptionsResult resultado = Validador.Validate(null, new EtlOpcoes { TamanhoMaximoDoPacote = -1 });

        resultado.Failures.Should().Equal("Geo:Etl:TamanhoMaximoDoPacote deve ser maior que zero.");
    }

    [Fact(DisplayName = "Só a quantidade de entradas inválida devolve só a falta da quantidade")]
    public void SoAQuantidade_DevolveSoAFaltaDaQuantidade()
    {
        ValidateOptionsResult resultado = Validador.Validate(null, new EtlOpcoes { QuantidadeMaximaDeEntradasDoPacote = 257 });

        resultado.Failures.Should().Equal("Geo:Etl:QuantidadeMaximaDeEntradasDoPacote deve estar entre 16 e 256.");
    }

    [Fact(DisplayName = "Só o diretório em branco devolve só a falta do diretório")]
    public void SoODiretorio_DevolveSoAFaltaDoDiretorio()
    {
        ValidateOptionsResult resultado = Validador.Validate(null, new EtlOpcoes { DiretorioDoPacote = string.Empty });

        resultado.Failures.Should().Equal("Geo:Etl:DiretorioDoPacote deve ser informado.");
    }

    [Fact(DisplayName = "Opções sem faltas dão sucesso e opções nulas lançam")]
    public void SemFaltas_DaSucesso_ENulasLancam()
    {
        Validador.Validate(null, new EtlOpcoes()).Should().Be(ValidateOptionsResult.Success);

        Action nulas = () => Validador.Validate(null, null!);

        nulas.Should().Throw<ArgumentNullException>();
    }
}
