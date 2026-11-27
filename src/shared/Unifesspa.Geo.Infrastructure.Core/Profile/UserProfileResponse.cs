namespace Unifesspa.Geo.Infrastructure.Core.Profile;

/// <summary>
/// Response payload for <c>GET /api/profile/me</c>. Exposes the authenticated user's
/// identity, as read from the access token.
/// </summary>
/// <param name="UserId">Subject identifier (Keycloak <c>sub</c> claim).</param>
/// <param name="Name">Display name, falling back to <c>preferred_username</c> when absent.</param>
/// <param name="Email">Email address, when present in the token.</param>
/// <param name="Roles">Realm roles resolved from the access token claims.</param>
/// <param name="Timestamp">Server-side clock read when the response was produced.</param>
public sealed record UserProfileResponse(
    string? UserId,
    string? Name,
    string? Email,
    IReadOnlyList<string> Roles,
    DateTimeOffset Timestamp);
