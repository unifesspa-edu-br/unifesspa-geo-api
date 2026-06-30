namespace Unifesspa.Geo.Infrastructure.Core.Smoke;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using StackExchange.Redis;

using Unifesspa.Geo.Infrastructure.Core.Caching;

using Wolverine;

/// <summary>
/// Endpoints smoke E2E para validação ponta-a-ponta de Cache (Redis) e Messaging
/// (Wolverine outbox + transport). Mapeados sob <c>/api/_smoke</c> e protegidos
/// por papel <c>admin</c> via policy <c>RequireRole</c>.
/// </summary>
/// <remarks>
/// <para>
/// Em produção esses endpoints permanecem ativos para diagnóstico operacional (on-call,
/// validação pós-deploy). Considerar feature flag para desabilitar em hardening posterior.
/// </para>
/// <para>
/// Authorization: <c>RequireAuthorization(policy =&gt; policy.RequireRole("admin"))</c>. O
/// claim role é populado pela <c>KeycloakRolesClaimsTransformation</c> (registrada por
/// <c>AddOidcAuthentication</c>) que mapeia o claim Keycloak <c>realm_access.roles</c> para
/// <c>ClaimTypes.Role</c>. A policy roda na pipeline de AuthZ middleware ANTES do model
/// binding — anônimos recebem 401, autenticados sem role admin recebem 403, sem hidratar
/// o corpo da requisição.
/// </para>
/// </remarks>
public static class SmokeEndpointsExtensions
{
    public const string AdminRole = "admin";
    public const string SmokeCacheKeyPrefix = "smoke:";
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Mapeia endpoints smoke sob <c>/api/_smoke</c>. Idempotente — chamar duas vezes
    /// causaria conflito de rotas, então deve ser chamado uma única vez por pipeline.
    /// </summary>
    public static IEndpointRouteBuilder MapGeoSmokeEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app
            .MapGroup("/api/_smoke")
            .RequireAuthorization(policy => policy.RequireRole(AdminRole))
            .WithTags("_smoke");

        // Cache: SET (random key + UTC now) com TTL 5min, retorna value para verificação.
        group.MapGet("/cache/{key}", ProbeSmokeCacheAsync)
            .WithName("smokeCacheProbe")
            .WithSummary("Smoke E2E — Cache probe")
            .WithDescription("Faz SET/GET de uma chave temporária no Redis com TTL 5min para validar conectividade. Restrito a usuários com role admin.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Messaging: publica SmokePingMessage via IMessageBus — handler em Infrastructure.Core
        // confirma round-trip via log.
        group.MapPost("/messaging/publish", PublishSmokeMessageAsync)
            .WithName("smokeMessagingPublish")
            .WithSummary("Smoke E2E — Messaging publish")
            .WithDescription("Publica um SmokePingMessage via Wolverine outbox para validar persistência + transport (PG queue durável). O handler em Infrastructure.Core registra log do round-trip. Restrito a usuários com role admin.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ProbeSmokeCacheAsync(
        string key,
        IConnectionMultiplexer redis,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();

        IDatabase db = redis.GetDatabase();
        string fullKey = SmokeCacheKeyPrefix + key;
        string value = timeProvider.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        await db.StringSetAsync(fullKey, value, DefaultCacheTtl).ConfigureAwait(false);
        RedisValue retrieved = await db.StringGetAsync(fullKey).ConfigureAwait(false);

        return Results.Ok(new
        {
            key = fullKey,
            value = retrieved.ToString(),
            ttlSeconds = (int)DefaultCacheTtl.TotalSeconds,
        });
    }

    private static async Task<IResult> PublishSmokeMessageAsync(
        IMessageBus bus,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(timeProvider);

        SmokePingMessage message = new(Guid.NewGuid(), timeProvider.GetUtcNow());
        await bus.PublishAsync(message).ConfigureAwait(false);

        return Results.Ok(new { id = message.Id, timestamp = message.Timestamp });
    }
}
