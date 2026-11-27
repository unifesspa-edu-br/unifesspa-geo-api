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
}
