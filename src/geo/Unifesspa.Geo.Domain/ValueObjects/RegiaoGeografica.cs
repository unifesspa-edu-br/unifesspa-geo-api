namespace Unifesspa.Geo.Domain.ValueObjects;

using Unifesspa.Geo.Domain.Entities;

/// <summary>Região do IBGE, com o código e o nome. Cada parte é aparada e, em branco, fica nula.</summary>
public readonly record struct RegiaoGeografica(string? Codigo, string? Nome)
{
    public string? Codigo { get; } = GeoTexto.NormalizarOpcional(Codigo);

    public string? Nome { get; } = GeoTexto.NormalizarOpcional(Nome);
}
