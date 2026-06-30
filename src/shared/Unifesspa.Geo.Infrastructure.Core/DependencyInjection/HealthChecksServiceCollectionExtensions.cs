namespace Unifesspa.Geo.Infrastructure.Core.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using Unifesspa.Geo.Infrastructure.Core.HealthChecks;

/// <summary>
/// Registra health checks agregados para Postgres e Redis. Pareado com a
/// configuração existente de <see cref="Authentication.OidcAuthenticationConfiguration"/>
/// (que já registra <c>OidcDiscoveryHealthCheck</c>) para compor o readiness probe completo
/// das APIs Uni+.
/// </summary>
public static class HealthChecksServiceCollectionExtensions
{
    /// <summary>
    /// Tag canônica usada para filtrar checks que respondem ao readiness probe. Endpoints
    /// devem usar <c>Predicate = h => h.Tags.Contains(<see cref="ReadyTag"/>)</c> em
    /// <see cref="Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions"/>.
    /// </summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Registra checks para as dependências infra das APIs Uni+ no
    /// <see cref="IHealthChecksBuilder"/> existente. Cada check é ativado condicionalmente
    /// pela presença da config correspondente — em Development sem alguma dep, o check é
    /// simplesmente não registrado em vez de reportar Unhealthy permanente.
    /// </summary>
    /// <param name="services">A coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    /// <param name="connectionStringName">Nome da connection string Postgres (PortalDb,
    /// SelecaoDb, IngressoDb).</param>
    /// <returns>A própria <paramref name="services"/> para encadeamento fluente.</returns>
    /// <remarks>
    /// <para>
    /// Pré-requisitos por check:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><b>Postgres</b>: usa a connection string nominal — ativa quando
    ///     <c>ConnectionStrings:{connectionStringName}</c> está preenchido.</description></item>
    ///   <item><description><b>Redis</b>: depende de <see cref="StackExchange.Redis.IConnectionMultiplexer"/>
    ///     registrado por <see cref="CacheServiceCollectionExtensions.AddGeoCache"/>.
    ///     Ativa quando <c>Redis:ConnectionString</c> está preenchido.</description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddGeoHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        IHealthChecksBuilder hc = services.AddHealthChecks();

        string? pgConn = configuration.GetConnectionString(connectionStringName);
        if (!string.IsNullOrWhiteSpace(pgConn))
        {
            hc.AddCheck(
                name: "postgres",
                instance: new PostgresHealthCheck(pgConn),
                tags: [ReadyTag, "db"]);
        }

        if (!string.IsNullOrWhiteSpace(configuration[$"{Caching.RedisOptions.SectionName}:{nameof(Caching.RedisOptions.ConnectionString)}"]))
        {
            hc.AddCheck<RedisHealthCheck>(
                name: "redis",
                tags: [ReadyTag, "cache"]);
        }

        return services;
    }
}
