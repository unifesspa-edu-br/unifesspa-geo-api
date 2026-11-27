namespace Unifesspa.Geo.IntegrationTests.Etl;

using NetTopologySuite.Geometries;

/// <summary>
/// Latitude, longitude e ponto como foram gravados. O ponto é arredondado na nona casa, bem além da
/// precisão das colunas de latitude e longitude (seis casas), só para a comparação não depender do
/// último dígito da conversão entre decimal e ponto flutuante.
/// </summary>
internal readonly record struct PosicaoGravada(decimal? Latitude, decimal? Longitude, double? PontoY, double? PontoX)
{
    private const int CasasDoPonto = 9;

    /// <summary>Posição que cabe no domínio geográfico: as colunas e o ponto trazem a mesma coordenada.</summary>
    public static PosicaoGravada Valida(decimal latitude, decimal longitude) =>
        new(latitude, longitude, Arredondar((double)latitude), Arredondar((double)longitude));

    /// <summary>Coordenada fora do domínio que a carga ainda grava nas colunas, sem ponto.</summary>
    public static PosicaoGravada SoNasColunas(decimal latitude, decimal longitude) =>
        new(latitude, longitude, null, null);

    public static PosicaoGravada Ausente => new(null, null, null, null);

    public static PosicaoGravada De(decimal? latitude, decimal? longitude, Point? ponto) =>
        new(latitude, longitude, Arredondar(ponto?.Y), Arredondar(ponto?.X));

    private static double? Arredondar(double? valor) =>
        valor is null ? null : Math.Round(valor.Value, CasasDoPonto);
}
