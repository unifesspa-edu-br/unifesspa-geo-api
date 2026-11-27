namespace Unifesspa.Geo.Domain.ValueObjects;

using Unifesspa.Geo.Domain.Entities;

/// <summary>
/// Unidade da federação dentro de um país: o identificador do país e a sigla da UF, aparada e em
/// maiúsculas. Texto nulo é recusado.
/// </summary>
public readonly record struct UfNoPais(Guid PaisId, string Sigla)
{
    public string Sigla { get; } = GeoTexto.NormalizarChaveMaiuscula(Sigla ?? throw new ArgumentNullException(nameof(Sigla)));
}
