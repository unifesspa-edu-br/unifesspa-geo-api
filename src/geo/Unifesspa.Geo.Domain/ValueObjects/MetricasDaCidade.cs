namespace Unifesspa.Geo.Domain.ValueObjects;

/// <summary>
/// As nove métricas do IBGE de uma cidade, com os mesmos nomes das propriedades do indicador da
/// cidade. Métrica sem valor fica nula.
/// </summary>
public sealed record MetricasDaCidade
{
    public decimal? AreaKm2 { get; init; }

    public int? PopulacaoResidente { get; init; }

    public decimal? DensidadeDemografica { get; init; }

    public decimal? Escolarizacao6a14 { get; init; }

    public decimal? Idh { get; init; }

    public decimal? MortalidadeInfantil { get; init; }

    public decimal? Receitas { get; init; }

    public decimal? Despesas { get; init; }

    public decimal? PibPerCapita { get; init; }
}
