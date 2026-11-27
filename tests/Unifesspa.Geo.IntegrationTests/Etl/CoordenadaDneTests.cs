namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Parsing;

/// <summary>
/// <see cref="CoordenadaDne"/>: lê latitude e longitude do texto da fonte e materializa a posição
/// com ponto SRID 4326, ordem PostGIS X=longitude / Y=latitude; ausência de qualquer componente,
/// texto que não é número ou valor fora do domínio geográfico → posição ausente.
/// </summary>
public sealed class CoordenadaDneTests
{
    [Fact(DisplayName = "lat/long válidos viram posição com Point 4326 com X=longitude e Y=latitude")]
    public void Ler_LatLongValidos_PontoComOrdemXLonYLat()
    {
        PosicaoGeografica posicao = CoordenadaDne.Ler("-23.55068", "-46.63413");

        posicao.Latitude.Should().Be(-23.55068m);
        posicao.Longitude.Should().Be(-46.63413m);
        Point? ponto = posicao.Ponto;
        ponto.Should().NotBeNull();
        ponto!.SRID.Should().Be(4326);
        ponto.X.Should().Be(-46.63413);
        ponto.Y.Should().Be(-23.55068);
    }

    [Fact(DisplayName = "Texto com mais de quinze algarismos significativos mantém o decimal lido nas colunas e o ponto com os mesmos valores")]
    public void Ler_MuitosAlgarismos_MantemODecimalLido()
    {
        PosicaoGeografica posicao = CoordenadaDne.Ler(" -5.3686704999999999 ", "-49.1173149");

        posicao.Latitude.Should().Be(-5.3686704999999999m);
        posicao.Longitude.Should().Be(-49.1173149m);
        posicao.Ponto!.Y.Should().Be((double)-5.3686704999999999m);
        posicao.Ponto.X.Should().Be((double)-49.1173149m);
    }

    [Theory(DisplayName = "Componente ausente ou que não é número deixa latitude, longitude e ponto vazios")]
    [InlineData(null, "-46.63413")]
    [InlineData("-23.55068", null)]
    [InlineData("-", "-")]
    [InlineData("abc", "-46.63413")]
    [InlineData("", "")]
    public void Ler_ComponenteAusenteOuInvalido_PosicaoAusente(string? latitude, string? longitude)
    {
        CoordenadaDne.Ler(latitude, longitude).Should().Be(PosicaoGeografica.Ausente);
    }

    [Theory(DisplayName = "Coordenada fora do domínio geográfico deixa latitude, longitude e ponto vazios (não estoura no geography nem na coluna)")]
    [InlineData("999", "999")]
    [InlineData("91", "-46.6")]
    [InlineData("-90.5", "0")]
    [InlineData("-23.5", "181")]
    [InlineData("0", "-180.5")]
    [InlineData("1234", "-50.5")]
    [InlineData("-5.5", "-200")]
    public void Ler_ForaDoDominioGeografico_PosicaoAusente(string latitude, string longitude)
    {
        CoordenadaDne.Ler(latitude, longitude).Should().Be(PosicaoGeografica.Ausente);
    }
}
