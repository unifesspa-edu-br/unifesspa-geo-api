namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.IntegrationTests.Fixtures.Hosting;

/// <summary>
/// Factory que preserva o pipeline produtivo <c>JwtBearer</c> para testes E2E de
/// autenticação contra Keycloak real via Testcontainers.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit fixtures e testes precisam instanciar a factory pública.")]
public sealed class GeoJwtApiFactory : ApiFactoryBase<Program>
{
    private readonly KeycloakContainerFixture _keycloak;

    public GeoJwtApiFactory(KeycloakContainerFixture keycloak)
    {
        _keycloak = keycloak;
    }

    /// <summary>
    /// No-op intencional: mantém o esquema produtivo JwtBearer registrado pelo
    /// <c>Program.cs</c>, em vez de trocar pelo <c>TestAuthHandler</c>.
    /// </summary>
    protected override void ConfigureTestAuthentication(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
    }

    protected override IEnumerable<KeyValuePair<string, string?>> GetConfigurationOverrides() =>
    [
        new("ConnectionStrings:GeoDb", "Host=localhost;Port=5432;Database=unifesspa_geo_auth_tests;Username=geo;Password=geo"),
        new("Auth:Authority", _keycloak.Authority),
        new("Auth:Audience", KeycloakContainerFixture.Audience),
        new("Auth:ValidateAudience", "false"),
        new("Geo:Etl:WorkerHabilitado", "false"),
        new("Redis:ConnectionString", string.Empty),
        new("Storage:Endpoint", string.Empty),
        new("Kafka:BootstrapServers", string.Empty),
    ];
}
