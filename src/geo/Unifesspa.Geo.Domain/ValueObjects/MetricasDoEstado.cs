namespace Unifesspa.Geo.Domain.ValueObjects;

/// <summary>
/// As nove métricas do IBGE de um estado, com os mesmos nomes das propriedades do indicador do
/// estado. Métrica sem valor fica nula.
/// </summary>
public sealed record MetricasDoEstado
{
    public decimal? AreaKm2 { get; init; }

    public int? PopulacaoResidente2022 { get; init; }

    public decimal? DensidadeDemografica { get; init; }

    public int? MatriculasEnsinoFundamental2023 { get; init; }

    public decimal? Idh { get; init; }

    public decimal? ReceitasBrutas { get; init; }

    public decimal? DespesasBrutas { get; init; }

    public int? RendimentoMensalPerCapita { get; init; }

    public int? TotalVeiculos2023 { get; init; }
}
