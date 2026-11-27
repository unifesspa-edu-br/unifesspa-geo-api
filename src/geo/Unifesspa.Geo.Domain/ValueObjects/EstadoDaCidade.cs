namespace Unifesspa.Geo.Domain.ValueObjects;

using Unifesspa.Geo.Domain.Entities;

/// <summary>
/// Estado a que uma cidade pertence: o identificador dele e a UF, aparada e em maiúsculas.
/// Texto nulo é recusado.
/// </summary>
public readonly record struct EstadoDaCidade(Guid EstadoId, string Uf)
{
    public string Uf { get; } = GeoTexto.NormalizarChaveMaiuscula(Uf ?? throw new ArgumentNullException(nameof(Uf)));
}
