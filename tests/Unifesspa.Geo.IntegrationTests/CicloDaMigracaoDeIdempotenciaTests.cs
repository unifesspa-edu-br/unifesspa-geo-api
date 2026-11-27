namespace Unifesspa.Geo.IntegrationTests;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Prova a ida e a volta da migração que retira a tabela <c>idempotency_cache</c>, num banco só deste
/// teste, criado no mesmo servidor da coleção: a volta recria a tabela como era e uma nova ida a retira,
/// sem mexer nas tabelas de dados do Geo nem nas linhas que elas já tinham.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CicloDaMigracaoDeIdempotenciaTests : IAsyncLifetime
{
    private const string NomeDoBanco = "uniplus_geo_ciclo_idempotencia";
    private const string TabelaRetirada = "idempotency_cache";

    private static readonly string[] ColunasEsperadas =
    [
        "body_hash|character|64|NO",
        "created_at|timestamp with time zone||NO",
        "endpoint|character varying|500|NO",
        "expires_at|timestamp with time zone||NO",
        "id|uuid||NO",
        "idempotency_key|character varying|255|NO",
        "response_body_cipher|bytea||YES",
        "response_headers_json|jsonb||YES",
        "response_status|integer||YES",
        "scope|character varying|255|NO",
        "status|smallint||NO",
    ];

    private readonly GeoPostgisFixture _fixture;
    private string _conexaoDoBanco = string.Empty;

    public CicloDaMigracaoDeIdempotenciaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await using GeoDbContext administrador = _fixture.CreateDbContext();
        await administrador.Database.ExecuteSqlRawAsync("DROP DATABASE IF EXISTS uniplus_geo_ciclo_idempotencia WITH (FORCE)");
        await administrador.Database.ExecuteSqlRawAsync("CREATE DATABASE uniplus_geo_ciclo_idempotencia");

        _conexaoDoBanco = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Database = NomeDoBanco,
            Pooling = false,
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        await using GeoDbContext administrador = _fixture.CreateDbContext();
        await administrador.Database.ExecuteSqlRawAsync("DROP DATABASE IF EXISTS uniplus_geo_ciclo_idempotencia WITH (FORCE)");
    }

    [Fact(DisplayName = "A volta da migração recria a tabela como era, e a nova ida a retira, sem mexer nos dados do Geo")]
    public async Task IdaEVoltaDaMigracao_RecriaARetiraATabelaSemMexerNosDados()
    {
        await using GeoDbContext contexto = CriarContexto();
        string[] migracoes = [.. contexto.Database.GetMigrations()];
        await contexto.Database.MigrateAsync();
        await GravarPaisAsync(contexto);
        string[] tabelasDeDados = await ListarTabelasDeDadosAsync(contexto);
        (await ListarTabelasAsync(contexto)).Should().NotContain(TabelaRetirada);

        await MigrarParaAsync(contexto, migracoes[^2]);
        await ConferirTabelaRecriadaAsync(contexto);
        await MigrarParaAsync(contexto, migracoes[^1]);

        (await ListarTabelasAsync(contexto)).Should().NotContain(TabelaRetirada);
        (await ListarTabelasDeDadosAsync(contexto)).Should().BeEquivalentTo(tabelasDeDados);
        (await contexto.Set<Pais>().CountAsync()).Should().Be(1);
    }

    private static async Task MigrarParaAsync(GeoDbContext contexto, string migracaoAlvo)
    {
        IMigrator migrador = contexto.GetService<IMigrator>();
        await migrador.MigrateAsync(migracaoAlvo);
    }

    private static async Task GravarPaisAsync(GeoDbContext contexto)
    {
        contexto.Set<Pais>().Add(GeoTestKeys.Ok(Pais.Importar(
            GeoTestKeys.SiglaIso(), "BR", "Brasil", null, null, null, null, "202601")));
        await contexto.SaveChangesAsync();
    }

    private static async Task ConferirTabelaRecriadaAsync(GeoDbContext contexto)
    {
        (await ListarTabelasAsync(contexto)).Should().Contain(TabelaRetirada);
        (await ListarColunasAsync(contexto)).Should().Equal(ColunasEsperadas);
        (await ListarChavesPrimariasAsync(contexto)).Should().Equal("pk_idempotency_cache");
        (await ListarIndicesAsync(contexto)).Should().BeEquivalentTo(
            "CREATE INDEX idx_idempotency_expires_at ON public.idempotency_cache USING btree (expires_at)",
            "CREATE UNIQUE INDEX idx_idempotency_lookup ON public.idempotency_cache USING btree (scope, endpoint, idempotency_key)",
            "CREATE UNIQUE INDEX pk_idempotency_cache ON public.idempotency_cache USING btree (id)");
    }

    private static async Task<string[]> ListarTabelasDeDadosAsync(GeoDbContext contexto) =>
        [.. (await ListarTabelasAsync(contexto)).Where(tabela => !string.Equals(tabela, TabelaRetirada, StringComparison.Ordinal))];

    private static async Task<string[]> ListarTabelasAsync(GeoDbContext contexto) =>
        await contexto.Database.SqlQuery<string>(
            $"""
            SELECT table_name AS "Value" FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name
            """).ToArrayAsync();

    private static async Task<string[]> ListarColunasAsync(GeoDbContext contexto) =>
        await contexto.Database.SqlQuery<string>(
            $"""
            SELECT column_name || '|' || data_type || '|' || COALESCE(character_maximum_length::text, '')
                   || '|' || is_nullable AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'idempotency_cache' ORDER BY column_name
            """).ToArrayAsync();

    private static async Task<string[]> ListarChavesPrimariasAsync(GeoDbContext contexto) =>
        await contexto.Database.SqlQuery<string>(
            $"""
            SELECT conname::text AS "Value" FROM pg_constraint
            WHERE conrelid = 'public.idempotency_cache'::regclass AND contype = 'p'
            """).ToArrayAsync();

    private static async Task<string[]> ListarIndicesAsync(GeoDbContext contexto) =>
        await contexto.Database.SqlQuery<string>(
            $"""
            SELECT indexdef AS "Value" FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'idempotency_cache'
            """).ToArrayAsync();

    private GeoDbContext CriarContexto() =>
        new(new DbContextOptionsBuilder<GeoDbContext>()
            .UseNpgsql(_conexaoDoBanco, npgsql =>
            {
                npgsql.UseNetTopologySuite();
                npgsql.MigrationsAssembly(typeof(GeoDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory");
            })
            .UseSnakeCaseNamingConvention()
            .Options);
}
