namespace Unifesspa.Geo.Application.Mappings;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;

public static class EstadoMapping
{
    public static EstadoDto ToDto(this Estado estado)
    {
        ArgumentNullException.ThrowIfNull(estado);
        return new EstadoDto(
            estado.Id,
            estado.Uf,
            estado.Nome,
            estado.Regiao,
            estado.Capital,
            estado.CodigoIbge,
            estado.Latitude,
            estado.Longitude);
    }
}
