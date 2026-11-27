namespace Unifesspa.Geo.Domain.ValueObjects;

using Unifesspa.Geo.Domain.Entities;

/// <summary>
/// Como um logradouro se chama: o tipo (rua, avenida), o nome, o nome completo e a forma de busca.
/// O nome é aparado e texto nulo é recusado; tipo e nome completo em branco ficam nulos; a forma de
/// busca vai em minúsculas e, em branco, fica nula.
/// </summary>
public readonly record struct NomeDoLogradouro(string? Tipo, string Nome, string? NomeCompleto, string? NomeNormalizado)
{
    public string? Tipo { get; } = GeoTexto.NormalizarOpcional(Tipo);

    public string Nome { get; } = Nome?.Trim() ?? throw new ArgumentNullException(nameof(Nome));

    public string? NomeCompleto { get; } = GeoTexto.NormalizarOpcional(NomeCompleto);

    public string? NomeNormalizado { get; } = GeoTexto.NormalizarBuscaOpcional(NomeNormalizado);
}
