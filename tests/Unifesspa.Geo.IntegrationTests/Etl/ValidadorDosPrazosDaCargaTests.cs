namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>Validação dos prazos da carga (duração máxima e tempo limite dos comandos) em <c>Geo:Etl</c>.</summary>
public sealed class ValidadorDosPrazosDaCargaTests
{
    private const string MensagemDaDuracao =
        "Geo:Etl:DuracaoMaximaDaCarga deve ser maior que zero e no máximo 49 dias.";

    private const string MensagemDoTempoLimite =
        "Geo:Etl:TempoLimiteDosComandosDaCarga deve ser maior que zero e no máximo 2147483 segundos.";

    private static readonly ValidadorDosPrazosDaCarga Validador = new();

    [Fact(DisplayName = "Os padrões de 1 hora e 30 minutos são válidos")]
    public void Padroes_SaoValidos()
    {
        EtlOpcoes opcoes = new();

        opcoes.DuracaoMaximaDaCarga.Should().Be(TimeSpan.FromHours(1));
        opcoes.TempoLimiteDosComandosDaCarga.Should().Be(TimeSpan.FromMinutes(30));
        Validador.Validate(null, opcoes).Succeeded.Should().BeTrue();
    }

    [Theory(DisplayName = "Duração máxima de 1 milissegundo a 49 dias é aceita")]
    [InlineData(1)]
    [InlineData(4233600000)]
    public void DuracaoNaFaixa_EhAceita(double milissegundos)
    {
        Validador.Validate(null, ComDuracao(milissegundos)).Succeeded.Should().BeTrue();
    }

    [Theory(DisplayName = "Duração máxima zero, negativa ou acima de 49 dias é recusada dizendo o que corrigir")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4233600001)]
    public void DuracaoForaDaFaixa_EhRecusada(double milissegundos)
    {
        ValidateOptionsResult resultado = Validador.Validate(null, ComDuracao(milissegundos));

        resultado.Failed.Should().BeTrue();
        resultado.Failures.Should().Equal(MensagemDaDuracao);
    }

    [Theory(DisplayName = "Tempo limite dos comandos de 1 milissegundo a 2147483 segundos é aceito")]
    [InlineData(1)]
    [InlineData(2147483000)]
    public void TempoLimiteNaFaixa_EhAceito(double milissegundos)
    {
        Validador.Validate(null, ComTempoLimite(milissegundos)).Succeeded.Should().BeTrue();
    }

    [Theory(DisplayName = "Tempo limite dos comandos zero, negativo ou que não cabe em segundos inteiros é recusado dizendo o que corrigir")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2147483001)]
    public void TempoLimiteForaDaFaixa_EhRecusado(double milissegundos)
    {
        ValidateOptionsResult resultado = Validador.Validate(null, ComTempoLimite(milissegundos));

        resultado.Failed.Should().BeTrue();
        resultado.Failures.Should().Equal(MensagemDoTempoLimite);
    }

    [Fact(DisplayName = "Os dois prazos inválidos são recusados juntos, cada um com a sua mensagem")]
    public void DoisPrazosInvalidos_AcumulamAsMensagens()
    {
        EtlOpcoes opcoes = new()
        {
            DuracaoMaximaDaCarga = TimeSpan.Zero,
            TempoLimiteDosComandosDaCarga = TimeSpan.FromSeconds(-5),
        };

        Validador.Validate(null, opcoes).Failures.Should().Equal(MensagemDaDuracao, MensagemDoTempoLimite);
    }

    private static EtlOpcoes ComDuracao(double milissegundos) =>
        new() { DuracaoMaximaDaCarga = TimeSpan.FromMilliseconds(milissegundos) };

    private static EtlOpcoes ComTempoLimite(double milissegundos) =>
        new() { TempoLimiteDosComandosDaCarga = TimeSpan.FromMilliseconds(milissegundos) };
}
