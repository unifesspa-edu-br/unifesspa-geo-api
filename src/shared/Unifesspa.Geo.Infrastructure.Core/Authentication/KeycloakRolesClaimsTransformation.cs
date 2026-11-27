namespace Unifesspa.Geo.Infrastructure.Core.Authentication;

using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;

/// <summary>
/// Transforma o claim Keycloak <c>realm_access</c> (objeto JSON com array <c>roles</c>) em
/// claims individuais do tipo <see cref="ClaimTypes.Role"/>. Sem esta transformação,
/// <c>RequireRole("admin")</c> não funciona porque a validação padrão do .NET busca
/// <c>ClaimTypes.Role</c>, que o JWT do Keycloak não emite no formato esperado.
/// </summary>
/// <remarks>
/// <para>
/// Idempotente — se o principal já tem <c>ClaimTypes.Role</c> populado, a transformação não
/// adiciona duplicatas. Roda em todo request autenticado (cache em memória do principal por
/// request — performance é aceitável para o volume das APIs Uni+).
/// </para>
/// <para>
/// Compatibilidade: o claim <c>realm_access</c> é o formato canônico do Keycloak para realm
/// roles. Para resource-specific roles (<c>resource_access.{client}.roles</c>), usar
/// <see cref="HttpUserContext.GetResourceRoles"/> diretamente — não há equivalência 1:1 a
/// uma policy nativa do .NET.
/// </para>
/// </remarks>
public sealed class KeycloakRolesClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        AcrescentarSemRepetir(identity, PapeisDoRealm(identity));

        return Task.FromResult(principal);
    }

    private static string[] PapeisDoRealm(ClaimsIdentity identity)
    {
        string? realmAccess = identity.FindFirst(KeycloakClaims.RealmAccess)?.Value;

        // Claim malformado (nulo) — provavelmente não-Keycloak: o principal segue sem mudança.
        return [.. (PapeisDoKeycloak.Ler(realmAccess, recurso: null, out _) ?? [])
            .Where(role => !string.IsNullOrWhiteSpace(role))];
    }

    // Adiciona apenas roles que ainda não estão presentes — idempotente em caso de
    // re-execução da pipeline (ex.: cookie auth + redirecionamento).
    private static void AcrescentarSemRepetir(ClaimsIdentity identity, string[] roles)
    {
        HashSet<string> existing = new(
            identity.FindAll(ClaimTypes.Role).Select(c => c.Value),
            StringComparer.Ordinal);

        foreach (string role in roles.Where(existing.Add))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }
    }
}
