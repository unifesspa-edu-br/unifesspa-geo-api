namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.ValueObjects;

public sealed class PosicaoGeograficaTests
{
    [Fact(DisplayName = "A posição guarda a latitude, a longitude e o ponto sem alterá-los")]
    public void Posicao_GuardaOsTresValores()
    {
        Point ponto = new(-49.11731, -5.36867) { SRID = 4326 };

        PosicaoGeografica posicao = new(-5.36867m, -49.11731m, ponto);

        (decimal? latitude, decimal? longitude, Point? pontoDaPosicao) = posicao;
        latitude.Should().Be(-5.36867m);
        longitude.Should().Be(-49.11731m);
        pontoDaPosicao.Should().BeSameAs(ponto);
    }

    [Fact(DisplayName = "A posição aceita a ausência de qualquer parte")]
    public void Posicao_AceitaAAusenciaDeQualquerParte()
    {
        PosicaoGeografica posicao = new(null, -49.11731m, null);

        posicao.Latitude.Should().BeNull();
        posicao.Longitude.Should().Be(-49.11731m);
        posicao.Ponto.Should().BeNull();
    }

    [Fact(DisplayName = "A posição validada com latitude e longitude no domínio traz o ponto 4326 com X=longitude e Y=latitude")]
    public void Validada_ComValoresNoDominio_CriaOPontoComOrdemXLonYLat()
    {
        PosicaoGeografica posicao = PosicaoGeografica.Validada(-23.55068m, -46.63413m);

        posicao.Latitude.Should().Be(-23.55068m);
        posicao.Longitude.Should().Be(-46.63413m);
        posicao.Ponto.Should().NotBeNull();
        posicao.Ponto!.SRID.Should().Be(4326);
        posicao.Ponto.X.Should().Be(-46.63413);
        posicao.Ponto.Y.Should().Be(-23.55068);
    }

    [Fact(DisplayName = "A posição validada mantém a latitude e a longitude lidas, sem passar por ponto flutuante, e o ponto traz os mesmos valores")]
    public void Validada_MantemODecimalOriginalLido()
    {
        PosicaoGeografica posicao = PosicaoGeografica.Validada(-5.3686704999999999m, -49.1173149m);

        posicao.Latitude.Should().Be(-5.3686704999999999m);
        posicao.Longitude.Should().Be(-49.1173149m);
        posicao.Ponto!.Y.Should().Be((double)-5.3686704999999999m);
        posicao.Ponto.X.Should().Be((double)-49.1173149m);
    }

    [Theory(DisplayName = "Os limites exatos de -90 a 90 e de -180 a 180 são válidos")]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    [InlineData(0, 0)]
    public void Validada_NosLimitesExatos_MantemAPosicao(int latitude, int longitude)
    {
        PosicaoGeografica posicao = PosicaoGeografica.Validada(latitude, longitude);

        posicao.Latitude.Should().Be(latitude);
        posicao.Longitude.Should().Be(longitude);
        posicao.Ponto.Should().NotBeNull();
    }

    [Theory(DisplayName = "Latitude ou longitude fora do domínio, inclusive 1234, deixa a posição ausente")]
    [InlineData(90.000001, 0)]
    [InlineData(-90.000001, 0)]
    [InlineData(0, 180.000001)]
    [InlineData(0, -180.000001)]
    [InlineData(1234, -50.5)]
    [InlineData(-5.5, 1234)]
    public void Validada_ForaDoDominio_PosicaoAusente(double latitude, double longitude)
    {
        PosicaoGeografica.Validada((decimal)latitude, (decimal)longitude).Should().Be(PosicaoGeografica.Ausente);
    }

    [Fact(DisplayName = "Só um valor inválido zera os dois: a longitude válida não fica gravada sozinha")]
    public void Validada_SoUmValorInvalido_ZeraOsDois()
    {
        PosicaoGeografica posicao = PosicaoGeografica.Validada(95.5m, -50.5m);

        posicao.Latitude.Should().BeNull();
        posicao.Longitude.Should().BeNull();
        posicao.Ponto.Should().BeNull();
    }

    [Theory(DisplayName = "Latitude ou longitude ausente deixa a posição ausente")]
    [InlineData(null, -50.5)]
    [InlineData(-5.5, null)]
    [InlineData(null, null)]
    public void Validada_ComValorAusente_PosicaoAusente(double? latitude, double? longitude)
    {
        PosicaoGeografica.Validada((decimal?)latitude, (decimal?)longitude).Should().Be(PosicaoGeografica.Ausente);
    }
}
