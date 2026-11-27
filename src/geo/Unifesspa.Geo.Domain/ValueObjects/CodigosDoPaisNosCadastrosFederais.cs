namespace Unifesspa.Geo.Domain.ValueObjects;

using Unifesspa.Geo.Domain.Entities;

/// <summary>
/// Códigos do país no Banco Central, na Receita Federal, no SPED e no Siscomex. Cada código é
/// aparado e, em branco, fica nulo.
/// </summary>
public readonly record struct CodigosDoPaisNosCadastrosFederais(
    string? CodigoBcb,
    string? CodigoRfb,
    string? CodigoSped,
    string? CodigoSiscomex)
{
    public string? CodigoBcb { get; } = GeoTexto.NormalizarOpcional(CodigoBcb);

    public string? CodigoRfb { get; } = GeoTexto.NormalizarOpcional(CodigoRfb);

    public string? CodigoSped { get; } = GeoTexto.NormalizarOpcional(CodigoSped);

    public string? CodigoSiscomex { get; } = GeoTexto.NormalizarOpcional(CodigoSiscomex);
}
