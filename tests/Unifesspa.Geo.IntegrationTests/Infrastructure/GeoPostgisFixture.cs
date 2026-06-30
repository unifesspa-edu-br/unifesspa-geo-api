namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;

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

        // Captura o valor prévio para restaurar no DisposeAsync — a env var é
        // process-wide e não pode vazar para outras coleções no mesmo processo.
        _connectionStringEnvVarPrevio = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);

        try
        {
            // Env var lida pelo WebApplicationBuilder a tempo (ConfigureAppConfiguration
            // chegaria tarde para a connection string lazy do DbContext/Wolverine).
            Environment.SetEnvironmentVariable(ConnectionStringEnvVar, ConnectionString);

            // Aplica o schema (extensão postgis + tabela-sonda + idempotency_cache).
            // Como superusuário do container, o CREATE EXTENSION postgis cria de fato.
            await using GeoDbContext context = CreateDbContext();
            await context.Database.MigrateAsync().ConfigureAwait(false);

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

    /// <summary>
    /// Constrói um <see cref="GeoDbContext"/> com NetTopologySuite ativo e os
    /// interceptors de produção (fallback de usuário "system").
    /// </summary>
    public GeoDbContext CreateDbContext()
    {
        DbContextOptions<GeoDbContext> options =
            new DbContextOptionsBuilder<GeoDbContext>()
                .UseNpgsql(ConnectionString, npgsql =>
                {
                    npgsql.UseNetTopologySuite();
                    // Mesmas convenções de migrations da produção (UseGeoNpgsqlConventions):
                    // sem este pin, a UseSnakeCaseNamingConvention poderia gravar a história
                    // numa tabela snake-cased divergente da `__EFMigrationsHistory` usada pelo
                    // host (AddGeoInfrastructure), fazendo a migration de startup ver a inicial
                    // como pendente e tentar recriar as tabelas (42P07).
                    npgsql.MigrationsAssembly(typeof(GeoDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory");
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(
                    new SoftDeleteInterceptor(TimeProvider.System, userContext: null),
                    new AuditableInterceptor(TimeProvider.System, userContext: null))
                .Options;

        return new GeoDbContext(options);
    }
}
