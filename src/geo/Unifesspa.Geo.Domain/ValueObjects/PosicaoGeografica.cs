namespace Unifesspa.Geo.Domain.ValueObjects;

using NetTopologySuite.Geometries;

/// <summary>
/// Posição de uma localidade ou de um endereço: latitude e longitude em graus e o ponto
/// georreferenciado (SRID 4326) que a carga monta a partir delas. Qualquer parte pode faltar.
/// </summary>
public readonly record struct PosicaoGeografica(decimal? Latitude, decimal? Longitude, Point? Ponto);
