namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>Distrito ou bairro como a fonte DNE o entrega, antes de qualquer resolução ou validação.</summary>
internal readonly record struct LocalidadeDaFonte(
    int? IdDne,
    string? Nome,
    string? NomeSemAcento,
    int? CidadeIdDne,
    string? Uf,
    string? Latitude,
    string? Longitude);
