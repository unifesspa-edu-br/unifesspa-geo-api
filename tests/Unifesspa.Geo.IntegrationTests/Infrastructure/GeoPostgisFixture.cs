namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

using Testcontainers.PostgreSql;

using Unifesspa.Geo.Infrastructure.Core.Persistence.Interceptors;
using Unifesspa.Geo.Infrastructure.Persistence;

/// <summary>
/// Provisiona um Postgres efêmero com <strong>PostGIS</strong> (Testcontainers,
/// imagem <c>postgis/postgis:18-3.6</c>), aplica o schema do
/// <see cref="GeoDbContext"/> via <c>MigrateAsync</c> (que cria a extensão
/// <c>postgis</c> — a conexão do container é superusuária) e expõe a
/// <see cref="GeoApiFactory"/> configurada por env var.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit ICollectionFixture<T> exige tipo público.")]
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Recursos liberados por IAsyncLifetime.DisposeAsync — xUnit invoca deterministicamente.")]
public sealed class GeoPostgisFixture : IAsyncLifetime
{
    private const string ConnectionStringEnvVar = "ConnectionStrings__GeoDb";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:18-3.6")
        .WithDatabase("uniplus_geo_tests")
        .WithUsername("uniplus_test")
        .WithPassword("uniplus_test")
        .Build();

    private string? _connectionStringEnvVarPrevio;
    private GeoApiFactory? _factory;

    public string ConnectionString => _postgres.GetConnectionString();

    public GeoApiFactory Factory => _factory
        ?? throw new InvalidOperationException("Fixture não inicializada — InitializeAsync não rodou.");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync().ConfigureAwait(false);
        _connectionStringEnvVarPrevio = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        try
        {
            PublicarAConnectionStringNoProcesso();
            await AplicarOSchemaAsync().ConfigureAwait(false);
            _factory = new GeoApiFactory();
        }
        catch
        {
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, _connectionStringEnvVarPrevio);
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync().ConfigureAwait(false);
        }

        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, _connectionStringEnvVarPrevio);

        await _postgres.DisposeAsync().ConfigureAwait(false);
    }

    // O valor prévio da env var é capturado em InitializeAsync para ser restaurado no DisposeAsync:
    // ela é process-wide e não pode vazar para outras coleções no mesmo processo.
    // Lida pelo WebApplicationBuilder a tempo (ConfigureAppConfiguration chegaria tarde
    // para a connection string lazy do DbContext/Wolverine).
    private void PublicarAConnectionStringNoProcesso() =>
        Environment.SetEnvironmentVariable(ConnectionStringEnvVar, ConnectionString);

    // Aplica o schema (extensão postgis + tabela-sonda).
    // Como superusuário do container, o CREATE EXTENSION postgis cria de fato.
    private async Task AplicarOSchemaAsync()
    {
        await using GeoDbContext context = CreateDbContext();
        await context.Database.MigrateAsync().ConfigureAwait(false);
    }

    private static void ConfigurarNpgsql(NpgsqlDbContextOptionsBuilder npgsql)
    {
        npgsql.UseNetTopologySuite();
        // Mesmas convenções de migrations da produção (UseGeoNpgsqlConventions):
        // sem este pin, a UseSnakeCaseNamingConvention poderia gravar a história
        // numa tabela snake-cased divergente da `__EFMigrationsHistory` usada pelo
        // host (AddGeoInfrastructure), fazendo a migration de startup ver a inicial
        // como pendente e tentar recriar as tabelas (42P07).
        npgsql.MigrationsAssembly(typeof(GeoDbContext).Assembly.FullName);
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory");
    }

    /// <summary>
    /// Constrói um <see cref="GeoDbContext"/> com NetTopologySuite ativo e os
    /// interceptors de produção (fallback de usuário "system").
    /// </summary>
    public GeoDbContext CreateDbContext() => CreateDbContext(TimeProvider.System, []);

    /// <summary>
    /// Igual a <see cref="CreateDbContext()"/>, com os carimbos de criação e atualização
    /// vindos do relógio informado: a carga compara esses carimbos com o instante em que iniciou.
    /// </summary>
    public GeoDbContext CreateDbContext(TimeProvider relogio) => CreateDbContext(relogio, []);

    /// <summary>
    /// Igual a <see cref="CreateDbContext()"/>, com interceptadores adicionais do EF Core
    /// para observar ou interromper pontos do próprio contexto sob teste.
    /// </summary>
    public GeoDbContext CreateDbContext(IReadOnlyList<IInterceptor> interceptadoresAdicionais) =>
        CreateDbContext(TimeProvider.System, interceptadoresAdicionais);

    /// <summary>Igual a <see cref="CreateDbContext()"/>, com o relógio e os interceptadores informados.</summary>
    public GeoDbContext CreateDbContext(TimeProvider relogio, IReadOnlyList<IInterceptor> interceptadoresAdicionais)
    {
        ArgumentNullException.ThrowIfNull(interceptadoresAdicionais);

        DbContextOptions<GeoDbContext> options =
            new DbContextOptionsBuilder<GeoDbContext>()
                .UseNpgsql(ConnectionString, ConfigurarNpgsql)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new AuditableInterceptor(relogio))
                .AddInterceptors([.. interceptadoresAdicionais])
                .Options;

        return new GeoDbContext(options);
    }
}
