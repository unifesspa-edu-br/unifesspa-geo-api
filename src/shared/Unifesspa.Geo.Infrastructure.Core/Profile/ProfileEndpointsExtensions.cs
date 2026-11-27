namespace Unifesspa.Geo.Infrastructure.Core.Profile;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Unifesspa.Geo.Application.Abstractions.Authentication;

/// <summary>
/// Extension methods that map shared profile endpoints onto the application pipeline.
/// </summary>
public static class ProfileEndpointsExtensions
{
    /// <summary>
    /// Maps the shared <c>/api/profile</c> endpoints (currently the authenticated-user
    /// profile <c>/me</c>).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static IEndpointRouteBuilder MapSharedProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder profileGroup = endpoints.MapGroup("/api/profile")
            .WithTags("Profile")
            .RequireAuthorization();

        MapearPerfilDoUsuario(profileGroup);

        return endpoints;
    }

    private static void MapearPerfilDoUsuario(RouteGroupBuilder grupo) =>
        grupo.MapGet("/me", PerfilDoUsuario)
            .WithName("GetAuthenticatedUserProfile")
            .WithSummary("Retorna o perfil do usuário autenticado")
            .WithDescription("Retorna identificador, nome, e-mail e papéis do usuário, extraídos do access token. Requer autenticação.")
            .Produces<UserProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

    private static IResult PerfilDoUsuario(IUserContext userContext, TimeProvider clock) =>
        Results.Ok(new UserProfileResponse(
            userContext.UserId,
            userContext.Name,
            userContext.Email,
            userContext.Roles,
            clock.GetUtcNow()));
}
