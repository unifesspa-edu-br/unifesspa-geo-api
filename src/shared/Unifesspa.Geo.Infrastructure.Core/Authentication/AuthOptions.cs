namespace Unifesspa.Geo.Infrastructure.Core.Authentication;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Bound options for JWT/Keycloak authentication. Validation runs at startup
/// via <c>ValidateDataAnnotations().ValidateOnStart()</c>.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// OIDC authority (Keycloak realm URL). Must be HTTPS outside Development.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [Url]
    public required string Authority { get; init; }

    /// <summary>
    /// Optional expected audience (<c>aud</c>) claim. Geo validates issuer/realm by
    /// default so any UNIFESSPA application in the configured realm can consume read
    /// endpoints; set <see cref="ValidateAudience"/> to require a specific client.
    /// </summary>
    public string? Audience { get; init; }

    /// <summary>
    /// Enables strict audience validation when an environment intentionally needs to
    /// restrict tokens to a specific client/application.
    /// </summary>
    public bool ValidateAudience { get; init; }

    /// <summary>
    /// Clock skew tolerance for token lifetime validation. Default 30s absorbs
    /// NTP drift between replicas without opening a meaningful window for
    /// expired tokens to be accepted.
    /// </summary>
    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromSeconds(30);
}
