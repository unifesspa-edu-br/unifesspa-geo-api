namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Filtros fixos de linha que a leitura do esquema de carga pode aplicar à tabela de logradouros.</summary>
internal enum FiltroDeLinhasDoEsquema
{
    /// <summary>Todas as linhas.</summary>
    Nenhum,

    /// <summary>Só as linhas sem tipo, nome nem texto completo: o CEP geral da cidade ou do distrito.</summary>
    SemNomeDeLogradouro,

    /// <summary>Só os logradouros de fato (o contrário de <see cref="SemNomeDeLogradouro"/>).</summary>
    ComNomeDeLogradouro,
}
