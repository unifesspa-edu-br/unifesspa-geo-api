namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>Validação do intervalo do sinal de vida da trava da carga em <c>Geo:Etl</c>.</summary>
public sealed class ValidadorDaTravaDaCargaTests
{
    private static readonly ValidadorDaTravaDaCarga Validador = new();

    [Fact(DisplayName = "O padrão do intervalo do sinal de vida é de 30 segundos e é válido")]
    public void Padrao_EhValido()
    {
        EtlOpcoes opcoes = new();

        opcoes.IntervaloDoSinalDeVidaDaTrava.Should().Be(TimeSpan.FromSeconds(30));
        Validador.Validate(null, opcoes).Succeeded.Should().BeTrue();
    }

    [Theory(DisplayName = "Intervalo de 1 segundo a 5 minutos é aceito")]
    [InlineData(1)]
    [InlineData(300)]
    public void IntervaloNaFaixa_EhAceito(double segundos)
    {
        Validador.Validate(null, ComIntervalo(segundos)).Succeeded.Should().BeTrue();
    }

    [Theory(DisplayName = "Intervalo zero, negativo ou acima de 5 minutos é recusado dizendo o que corrigir")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.5)]
    [InlineData(301)]
    public void IntervaloForaDaFaixa_EhRecusado(double segundos)
    {
        ValidateOptionsResult resultado = Validador.Validate(null, ComIntervalo(segundos));

        resultado.Failed.Should().BeTrue();
        resultado.FailureMessage.Should().Be(
            "Geo:Etl:IntervaloDoSinalDeVidaDaTrava deve estar entre 1 segundo e 5 minutos.");
    }

    private static EtlOpcoes ComIntervalo(double segundos) =>
        new() { IntervaloDoSinalDeVidaDaTrava = TimeSpan.FromSeconds(segundos) };
}
