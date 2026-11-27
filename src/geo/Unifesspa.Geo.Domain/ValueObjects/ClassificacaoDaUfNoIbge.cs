namespace Unifesspa.Geo.Domain.ValueObjects;

using Unifesspa.Geo.Domain.Entities;

/// <summary>Código e região da UF no IBGE. Cada parte é aparada e, em branco, fica nula.</summary>
public readonly record struct ClassificacaoDaUfNoIbge(string? CodigoIbge, string? Regiao)
{
    public string? CodigoIbge { get; } = GeoTexto.NormalizarOpcional(CodigoIbge);

    public string? Regiao { get; } = GeoTexto.NormalizarOpcional(Regiao);
}
