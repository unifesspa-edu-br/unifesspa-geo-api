namespace Unifesspa.Geo.Domain.ValueObjects;

using Unifesspa.Geo.Domain.Entities;

/// <summary>
/// Faixa de CEP, do inicial ao final. Cada limite é aparado e, em branco, fica nulo.
/// </summary>
public readonly record struct FaixaDeCep(string? CepInicial, string? CepFinal)
{
    public string? CepInicial { get; } = GeoTexto.NormalizarOpcional(CepInicial);

    public string? CepFinal { get; } = GeoTexto.NormalizarOpcional(CepFinal);

    public bool TemCepInicial => CepInicial is not null;

    public bool TemCepFinal => CepFinal is not null;
}
