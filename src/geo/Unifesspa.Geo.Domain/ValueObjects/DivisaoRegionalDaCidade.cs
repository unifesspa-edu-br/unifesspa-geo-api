namespace Unifesspa.Geo.Domain.ValueObjects;

/// <summary>As quatro regiões do IBGE a que uma cidade pertence.</summary>
public readonly record struct DivisaoRegionalDaCidade(
    RegiaoGeografica Mesorregiao,
    RegiaoGeografica Microrregiao,
    RegiaoGeografica RegiaoIntermediaria,
    RegiaoGeografica RegiaoImediata);
