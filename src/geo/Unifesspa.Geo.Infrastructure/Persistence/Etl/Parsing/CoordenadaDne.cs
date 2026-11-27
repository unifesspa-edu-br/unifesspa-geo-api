namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Parsing;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.ValueObjects;

/// <summary>
/// Posição de uma linha da DNE, lida do texto da fonte com parse tolerante: coordenada ausente
/// ou inválida vira <see langword="null"/>.
/// </summary>
internal static class CoordenadaDne
{
    public static PosicaoGeografica De(string? latitude, string? longitude) =>
        new(
            ParseTolerante.ParaDecimal(latitude),
            ParseTolerante.ParaDecimal(longitude),
            PontoFactory.Criar(latitude, longitude));

    /// <summary>
    /// Latitude e longitude derivadas do ponto já validado (limitado a ±90/±180): coordenada
    /// fora do domínio some por inteiro, em vez de estourar a coluna numérica.
    /// </summary>
    public static PosicaoGeografica DoPontoValidado(string? latitude, string? longitude)
    {
        Point? ponto = PontoFactory.Criar(latitude, longitude);
        return new PosicaoGeografica((decimal?)ponto?.Y, (decimal?)ponto?.X, ponto);
    }
}
