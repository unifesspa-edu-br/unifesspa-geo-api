namespace Unifesspa.Geo.IntegrationTests.Etl;

using NetTopologySuite.Geometries;

/// <summary>
/// Latitude, longitude e ponto como foram gravados. O ponto é comparado exatamente (X, Y e SRID):
/// o banco guarda o ponto como números de ponto flutuante de 64 bits, os mesmos enviados pela carga.
/// </summary>
internal readonly record struct PosicaoGravada(decimal? Latitude, decimal? Longitude, double? PontoY, double? PontoX, int? Srid)
{
    private const int Srid4326 = 4326;

    /// <summary>Posição dentro do domínio: as colunas trazem o valor esperado e o ponto traz o valor enviado.</summary>
    public static PosicaoGravada Valida(decimal latitude, decimal longitude) =>
        Valida(latitude, longitude, latitude, longitude);

    /// <summary>Posição dentro do domínio cujas colunas diferem do valor enviado (o banco arredonda a sexta casa).</summary>
    public static PosicaoGravada Valida(decimal latitudeColuna, decimal longitudeColuna, decimal latitudeEnviada, decimal longitudeEnviada) =>
        new(latitudeColuna, longitudeColuna, (double)latitudeEnviada, (double)longitudeEnviada, Srid4326);

    /// <summary>Coordenada ausente ou fora do domínio: latitude, longitude, ponto e SRID vazios.</summary>
    public static PosicaoGravada Ausente => new(null, null, null, null, null);

    public static PosicaoGravada De(decimal? latitude, decimal? longitude, Point? ponto) =>
        new(latitude, longitude, ponto?.Y, ponto?.X, ponto?.SRID);
}
