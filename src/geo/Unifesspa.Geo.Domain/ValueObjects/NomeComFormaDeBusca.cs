namespace Unifesspa.Geo.Domain.ValueObjects;

using Unifesspa.Geo.Domain.Entities;

/// <summary>
/// Nome de uma localidade e a forma dele usada na busca (sem acento, na base dos Correios). O nome
/// é aparado e texto nulo é recusado; a forma de busca vai em minúsculas e, em branco, fica nula.
/// </summary>
public readonly record struct NomeComFormaDeBusca(string Nome, string? NomeNormalizado)
{
    public string Nome { get; } = Nome?.Trim() ?? throw new ArgumentNullException(nameof(Nome));

    public string? NomeNormalizado { get; } = GeoTexto.NormalizarBuscaOpcional(NomeNormalizado);
}
