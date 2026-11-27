namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Parsing;

using NetTopologySuite.Geometries;

/// <summary>
/// Latitude, longitude e ponto geográfico de uma linha da DNE, lidos do texto da fonte
/// com parse tolerante: coordenada ausente ou inválida vira <see langword="null"/>.
/// </summary>
internal readonly record struct CoordenadaDne(decimal? Latitude, decimal? Longitude, Point? Ponto)
{
    public static CoordenadaDne De(string? latitude, string? longitude) =>
        new(
            ParseTolerante.ParaDecimal(latitude),
            ParseTolerante.ParaDecimal(longitude),
            PontoFactory.Criar(latitude, longitude));

    /// <summary>
    /// Latitude e longitude derivadas do ponto já validado (limitado a ±90/±180): coordenada
    /// fora do domínio some por inteiro, em vez de estourar a coluna numérica.
    /// </summary>
    public static CoordenadaDne DoPontoValidado(string? latitude, string? longitude)
    {
        Point? ponto = PontoFactory.Criar(latitude, longitude);
        return new CoordenadaDne((decimal?)ponto?.Y, (decimal?)ponto?.X, ponto);
    }
}
