namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using System.Data.Common;
using System.Globalization;

/// <summary>Consulta o catálogo do Postgres do banco de testes (índices, colunas e extensões).</summary>
internal sealed class CatalogoDoEsquema
{
    private const string ConsultaDeColuna =
        """
        SELECT data_type, is_nullable, is_generated, generation_expression
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = @tabela
          AND column_name = @coluna;
        """;

    private readonly GeoPostgisFixture _fixture;

    public CatalogoDoEsquema(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task<string?> ObterIndexDefAsync(string indexName)
    {
        await using ConsultaAoCatalogo consulta = new(_fixture);
        await consulta.AbrirAsync();
        consulta.Comando.CommandText = "SELECT indexdef FROM pg_indexes WHERE indexname = @nome;";
        consulta.AdicionarParametro("nome", indexName);

        object? result = await consulta.Comando.ExecuteScalarAsync();
        return result as string;
    }

    public async Task<ColunaDoEsquema?> ObterColunaAsync(string tabela, string coluna)
    {
        await using ConsultaAoCatalogo consulta = new(_fixture);
        await consulta.AbrirAsync();
        consulta.Comando.CommandText = ConsultaDeColuna;
        consulta.AdicionarParametro("tabela", tabela);
        consulta.AdicionarParametro("coluna", coluna);

        await using DbDataReader reader = await consulta.Comando.ExecuteReaderAsync();
        return await reader.ReadAsync() ? LerColuna(reader) : null;
    }

    public async Task<long> ContarIndicesUnicosSomenteCepAsync()
    {
        await using ConsultaAoCatalogo consulta = new(_fixture);
        await consulta.AbrirAsync();
        // Índice UNIQUE cujo conjunto de colunas é exatamente "(cep)" — a vírgula em
        // "(cep, ..." de um índice composto não casa com "(cep)".
        consulta.Comando.CommandText =
            "SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'logradouro' "
            + "AND indexdef ILIKE '%UNIQUE%' AND indexdef ILIKE '%(cep)%';";
        return Convert.ToInt64(await consulta.Comando.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    public async Task<long> ContarExtensaoPgTrgmAsync()
    {
        await using ConsultaAoCatalogo consulta = new(_fixture);
        await consulta.AbrirAsync();
        consulta.Comando.CommandText = "SELECT COUNT(*) FROM pg_extension WHERE extname = 'pg_trgm';";
        return Convert.ToInt64(await consulta.Comando.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static ColunaDoEsquema LerColuna(DbDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? string.Empty : reader.GetString(3));
}
