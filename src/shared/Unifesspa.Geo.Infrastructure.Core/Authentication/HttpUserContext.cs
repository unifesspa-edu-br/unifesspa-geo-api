namespace Unifesspa.Geo.Infrastructure.Core.Authentication;

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Application.Abstractions.Authentication;

/// <summary>
/// Resolves authenticated user data from the current HTTP context.
/// </summary>
public sealed partial class HttpUserContext : IUserContext
{
    private static readonly StringComparer RoleComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] UserIdClaimCandidates = [ClaimTypes.NameIdentifier, OidcClaims.Sub];
    private static readonly string[] NameClaimCandidates = [ClaimTypes.Name, OidcClaims.Name, OidcClaims.PreferredUsername];
    private static readonly string[] EmailClaimCandidates = [ClaimTypes.Email, OidcClaims.Email];
    private static readonly string[] DirectRoleClaimTypes = [ClaimTypes.Role, KeycloakClaims.Role, KeycloakClaims.Roles];

    // plataforma-admin é o bypass platform-wide (IsPlataformaAdmin).
    private const string PlataformaAdminRole = "plataforma-admin";

    private readonly ClaimsPrincipal? _user;
    private readonly ILogger<HttpUserContext> _logger;
    private readonly Lazy<IReadOnlyList<string>> _roles;
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _resourceRolesCache = new(StringComparer.Ordinal);

    public HttpUserContext(
        IHttpContextAccessor httpContextAccessor,
        ILogger<HttpUserContext> logger)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        _user = httpContextAccessor.HttpContext?.User;
        _logger = logger;
        _roles = new Lazy<IReadOnlyList<string>>(ResolveRoles);
    }

    public bool IsAuthenticated => _user?.Identity?.IsAuthenticated == true;

    public string? UserId => GetFirstClaimValue(UserIdClaimCandidates);

    public string? Name => GetFirstClaimValue(NameClaimCandidates);

    public string? Email => GetFirstClaimValue(EmailClaimCandidates);

    public IReadOnlyList<string> Roles => _roles.Value;

    public bool IsPlataformaAdmin => HasRole(PlataformaAdminRole);

    public bool HasRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        return Roles.Contains(role, RoleComparer);
    }

    public IReadOnlyList<string> GetResourceRoles(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        return _resourceRolesCache.GetOrAdd(resourceName, ResolveResourceRoles);
    }

    private IReadOnlyList<string> ResolveResourceRoles(string resourceName) =>
        ParseRolesFromJsonClaim(
                KeycloakClaims.ResourceAccess,
                _user?.FindFirst(KeycloakClaims.ResourceAccess)?.Value,
                resourceName)
            .Distinct(RoleComparer)
            .ToArray();

    private string? GetFirstClaimValue(string[] claimTypes) =>
        _user is null
            ? null
            : claimTypes
                .Select(claimType => _user.FindFirst(claimType)?.Value)
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private string[] ResolveRoles() =>
        EnumerateDirectRoleClaims()
            .Concat(ParseRolesFromJsonClaim(
                KeycloakClaims.RealmAccess,
                _user?.FindFirst(KeycloakClaims.RealmAccess)?.Value,
                resourceName: null))
            .Where(static role => !string.IsNullOrWhiteSpace(role))
            .Distinct(RoleComparer)
            .ToArray();

    private IEnumerable<string> EnumerateDirectRoleClaims()
    {
        if (_user is null)
        {
            yield break;
        }

        foreach (string claimType in DirectRoleClaimTypes)
        {
            foreach (Claim claim in _user.FindAll(claimType))
            {
                yield return claim.Value;
            }
        }
    }

    private string[] ParseRolesFromJsonClaim(string claimName, string? claimValue, string? resourceName)
    {
        string[]? papeis = PapeisDoKeycloak.Ler(claimValue, resourceName, out JsonException? falha);
        if (papeis is null)
        {
            LogMalformedClaim(_logger, claimName, resourceName, falha);
        }

        return papeis ?? [];
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Falha ao fazer parse do claim {ClaimName} (recurso {ResourceName}); roles ignoradas.")]
    private static partial void LogMalformedClaim(
        ILogger logger,
        string claimName,
        string? resourceName,
        Exception? exception);
}
