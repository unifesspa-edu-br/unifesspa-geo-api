namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using Npgsql;

using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>Configuração e consultas diretas ao banco da coleção, usadas para conferir esquemas de carga.</summary>
internal sealed class ConexaoDoBancoDaCarga
{
    private readonly GeoPostgisFixture _fixture;

    public ConexaoDoBancoDaCarga(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public IConfiguration Configuracao => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ConnectionStrings:GeoDb"] = _fixture.ConnectionString })
        .Build();

    public async Task<bool> ExisteEsquemaAsync(string esquema) =>
        await EscalarAsync("SELECT count(*) AS \"Value\" FROM pg_namespace WHERE nspname = @valor", esquema) == 1;

    public Task<long> TabelasSemRegistroDeTransacaoAsync(string esquema) =>
        EscalarAsync(
            """
            SELECT count(*) AS "Value" FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @valor AND c.relkind = 'r' AND c.relpersistence = 'u'
            """,
            esquema);

    public Task<long> ColunasComNomeAsync(string esquema, string nome) =>
        EscalarAsync(
            "SELECT count(*) AS \"Value\" FROM information_schema.columns WHERE table_schema = @valor AND column_name = @nome",
            esquema,
            nome);

    /// <summary>Sessões deste banco, que não a de quem pergunta, com transação aberta e parada neste instante.</summary>
    public Task<long> SessoesComTransacaoAbertaAsync() =>
        EscalarAsync(
            """
            SELECT count(*) AS "Value" FROM pg_stat_activity
            WHERE datname = current_database() AND pid <> pg_backend_pid() AND state LIKE 'idle in transaction%'
            """);

    public Task<long> LinhasAsync(string esquema, string tabela) =>
        EscalarAsync($"SELECT count(*) AS \"Value\" FROM \"{esquema}\".\"{tabela}\"");

    public async Task<List<string>> TextosAsync(string esquema, string tabela, string coluna)
    {
        string consulta = $"SELECT \"{coluna}\" AS \"Value\" FROM \"{esquema}\".\"{tabela}\"";
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        return await contexto.Database.SqlQueryRaw<string>(consulta).ToListAsync();
    }

    public async Task ExecutarAsync(string comando)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        await contexto.Database.ExecuteSqlRawAsync(comando);
    }

    private async Task<long> EscalarAsync(string consulta, string? valor = null, string? nome = null)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        NpgsqlParameter[] parametros = valor is null ? [] : [new NpgsqlParameter("valor", valor)];
        if (nome is not null)
        {
            parametros = [.. parametros, new NpgsqlParameter("nome", nome)];
        }

        return await contexto.Database.SqlQueryRaw<long>(consulta, parametros).SingleAsync();
    }
}
