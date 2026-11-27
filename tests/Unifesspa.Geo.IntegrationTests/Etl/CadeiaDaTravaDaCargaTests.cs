namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Npgsql;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>A conexão da trava da carga leva o sinal de vida configurado, sobre a cadeia do banco do Geo.</summary>
public sealed class CadeiaDaTravaDaCargaTests
{
    private const string BancoDoGeo = "Host=banco.exemplo;Port=5432;Database=geo_exemplo;Username=usuario_exemplo";

    [Fact(DisplayName = "A cadeia da trava leva o intervalo do sinal de vida configurado, no mesmo servidor e banco do Geo")]
    public void CadeiaDaTrava_LevaOIntervaloConfigurado()
    {
        EtlOpcoes opcoes = new() { IntervaloDoSinalDeVidaDaTrava = TimeSpan.FromSeconds(45) };

        NpgsqlConnectionStringBuilder cadeia = CadeiaMontada(opcoes);

        cadeia.KeepAlive.Should().Be(45);
        cadeia.Host.Should().Be("banco.exemplo");
        cadeia.Database.Should().Be("geo_exemplo");
    }

    [Fact(DisplayName = "Sem o intervalo configurado, a cadeia da trava leva o padrão de 30 segundos")]
    public void SemConfiguracao_CadeiaLevaOPadrao()
    {
        CadeiaMontada(new EtlOpcoes()).KeepAlive.Should().Be(30);
    }

    [Fact(DisplayName = "Fração de segundo do intervalo é arredondada para cima")]
    public void FracaoDeSegundo_ArredondaParaCima()
    {
        EtlOpcoes opcoes = new() { IntervaloDoSinalDeVidaDaTrava = TimeSpan.FromMilliseconds(1500) };

        CadeiaMontada(opcoes).KeepAlive.Should().Be(2);
    }

    [Theory(DisplayName = "A cadeia da trava difere da do Geo e leva o nome de aplicação da trava, mesmo que a do Geo já traga os mesmos parâmetros")]
    [InlineData(BancoDoGeo)]
    [InlineData(BancoDoGeo + ";Keepalive=30;Application Name=outra-aplicacao")]
    public void CadeiaDaTrava_DifereDaCadeiaDoGeo(string cadeiaDoGeo)
    {
        string cadeiaDaTrava = TravaDaCargaParaTeste.Criar(cadeiaDoGeo, new EtlOpcoes()).MontarTextoDeConexao();

        cadeiaDaTrava.Should().NotBe(cadeiaDoGeo);
        new NpgsqlConnectionStringBuilder(cadeiaDaTrava).ApplicationName
            .Should().Be(TravaConsultivaDaCargaDne.NomeDaAplicacaoDaTrava);
    }

    private static NpgsqlConnectionStringBuilder CadeiaMontada(EtlOpcoes opcoes) =>
        new(TravaDaCargaParaTeste.Criar(BancoDoGeo, opcoes).MontarTextoDeConexao());
}
