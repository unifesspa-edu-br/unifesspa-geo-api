namespace Unifesspa.Geo.Application.Queries.Proximidade;

using Unifesspa.Geo.Application.Abstractions.Messaging;
using Unifesspa.Geo.Application.DTOs;

/// <summary>
/// Busca as cidades vigentes dentro de <see cref="RaioKm"/> do ponto
/// (<see cref="Latitude"/>, <see cref="Longitude"/>), ordenadas por distância
/// crescente, limitadas ao top-<see cref="Limit"/> (ranking por distância — sem cursor).
/// </summary>
public sealed record BuscarCidadesProximasQuery(
    double Latitude,
    double Longitude,
    double RaioKm,
    int Limit) : IQuery<IReadOnlyList<CidadeProximaDto>>;
