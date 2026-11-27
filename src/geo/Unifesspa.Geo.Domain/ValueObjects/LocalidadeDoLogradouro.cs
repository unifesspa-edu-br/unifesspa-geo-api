namespace Unifesspa.Geo.Domain.ValueObjects;

using Unifesspa.Geo.Domain.Entities;

/// <summary>
/// Onde um logradouro fica: a cidade, o distrito e o bairro, estes dois opcionais, e a UF, aparada
/// e em maiúsculas. Texto nulo é recusado.
/// </summary>
public readonly record struct LocalidadeDoLogradouro(Guid CidadeId, Guid? DistritoId, Guid? BairroId, string Uf)
{
    public string Uf { get; } = GeoTexto.NormalizarChaveMaiuscula(Uf ?? throw new ArgumentNullException(nameof(Uf)));
}
