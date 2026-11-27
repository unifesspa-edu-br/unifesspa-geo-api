namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.Extensions.Configuration;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

/// <summary>A leitura do prazo dos comandos da carga recusa valor fora da faixa, mesmo sem passar pela partida.</summary>
public sealed class PrazoDosComandosNaConfiguracaoDaCargaTests
{
    [Theory(DisplayName = "Prazo dos comandos zero, negativo ou acima do teto é recusado citando a opção")]
    [InlineData("00:00:00")]
    [InlineData("-00:00:05")]
    [InlineData("24.20:31:24")]
    public void PrazoForaDaFaixa_ERecusado(string prazo)
    {
        Func<ConfiguracaoDaCargaEmLote> ler = () => Ler(prazo);

        ler.Should().Throw<InvalidOperationException>()
            .WithMessage("*Geo:Etl:TempoLimiteDosComandosDaCarga*maior que zero*");
    }

    [Theory(DisplayName = "Prazo dos comandos válido vira segundos inteiros, arredondado para cima")]
    [InlineData("00:00:05", 5)]
    [InlineData("00:00:01.500", 2)]
    public void PrazoValido_ViraSegundosArredondadosParaCima(string prazo, int segundos)
    {
        Ler(prazo).PrazoDosComandosEmSegundos.Should().Be(segundos);
    }

    private static ConfiguracaoDaCargaEmLote Ler(string prazo)
    {
        IConfiguration configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.TempoLimiteDosComandosDaCarga)}"] = prazo,
            })
            .Build();

        return ConfiguracaoDaCargaEmLote.Ler(configuracao);
    }
}
