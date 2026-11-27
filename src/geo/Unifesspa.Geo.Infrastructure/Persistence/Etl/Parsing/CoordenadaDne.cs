namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Parsing;

using Unifesspa.Geo.Domain.ValueObjects;

/// <summary>
/// Posição de uma linha da DNE, lida do texto da fonte com parse tolerante: coordenada ausente,
/// não numérica ou fora do domínio geográfico vira posição ausente (latitude, longitude e ponto
/// vazios), e nunca derruba a carga.
/// </summary>
internal static class CoordenadaDne
{
    public static PosicaoGeografica Ler(string? latitude, string? longitude) =>
        PosicaoGeografica.Validada(ParseTolerante.ParaDecimal(latitude), ParseTolerante.ParaDecimal(longitude));
}
