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
}
