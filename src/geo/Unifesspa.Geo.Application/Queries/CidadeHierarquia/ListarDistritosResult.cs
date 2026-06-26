namespace Unifesspa.Geo.Application.Queries.CidadeHierarquia;

using Unifesspa.Geo.Application.DTOs;

/// <summary>
/// Resultado da listagem de distritos de uma Cidade.
/// </summary>
public sealed record ListarDistritosResult(
    bool CidadeExiste,
    IReadOnlyList<DistritoDto> Items,
    Guid? AnteriorAfterId,
    Guid? ProximoAfterId);
