namespace Unifesspa.Geo.Infrastructure.Core.Authentication;

using HealthChecks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Unifesspa.Geo.Application.Abstractions.Authentication;

/// <summary>
/// Extension methods for configuring OIDC/JWT Bearer authentication.
/// Validation is OIDC-standard (issuer/realm, signing key, lifetime); audience
/// validation is optional because Geo is consumed by multiple UNIFESSPA applications
/// in the same configured realm.
/// </summary>
public static class OidcAuthenticationConfiguration
{
    /// <summary>
    /// Binds <see cref="AuthOptions"/> and configures JWT Bearer authentication against the OIDC authority.
    /// Options are validated at application startup (required fields, URL format, HTTPS outside Development).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The hosting environment — controls HTTPS metadata enforcement.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddOidcAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        bool requireHttps = !environment.IsDevelopment();

        AddOpcoesDeAutenticacao(services, configuration, requireHttps);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        ConfigurarJwtBearer(services, requireHttps);
        AddContextoDoUsuario(services);
        AddVerificacaoDoProvedorDeIdentidade(services);

        return services.AddAuthorization();
    }

    private static void AddOpcoesDeAutenticacao(IServiceCollection services, IConfiguration configuration, bool requireHttps)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => !requireHttps
                    || options.Authority is not string authority
                    || !authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
                "Auth:Authority must use HTTPS outside Development.")
            .Validate(
                options => !options.ValidateAudience || !string.IsNullOrWhiteSpace(options.Audience),
                "Auth:Audience must be set when Auth:ValidateAudience is true.")
            .ValidateOnStart();
    }

    private static void ConfigurarJwtBearer(IServiceCollection services, bool requireHttps)
    {
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>, ILoggerFactory>((jwtOptions, authAccessor, loggerFactory) =>
                AplicarOpcoesDoJwt(jwtOptions, authAccessor.Value, requireHttps, loggerFactory));

        // IProblemDetailsService disponibiliza `CustomizeProblemDetails` e o
        // writer único usado pelos hooks de JwtBearer (challenge/forbidden).
        // Sem este registro, JwtBearerProblemDetailsEvents cai no fallback
        // manual — funcional, mas sem enriquecimento global.
        services.AddProblemDetails();
    }

    private static void AplicarOpcoesDoJwt(JwtBearerOptions jwtOptions, AuthOptions auth, bool requireHttps, ILoggerFactory loggerFactory)
    {
        jwtOptions.Authority = auth.Authority;
        jwtOptions.Audience = auth.ValidateAudience ? auth.Audience : null;
        jwtOptions.RequireHttpsMetadata = requireHttps;
        jwtOptions.MapInboundClaims = false;
        jwtOptions.TokenValidationParameters = ValidacaoDoToken(auth);
        jwtOptions.Events
            .WithStructuredLogging(
                loggerFactory.CreateLogger("Unifesspa.Geo.Authentication.JwtBearer"))
            .WithProblemDetails();
    }

    private static TokenValidationParameters ValidacaoDoToken(AuthOptions auth) =>
        new()
        {
            ValidateIssuer = true,
            ValidateAudience = auth.ValidateAudience,
            ValidAudience = auth.ValidateAudience ? auth.Audience : null,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = auth.ClockSkew,
        };

    private static void AddContextoDoUsuario(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, HttpUserContext>();

        // Mapeia realm_access.roles (formato Keycloak) → ClaimTypes.Role para que
        // RequireRole("...") em policies funcione nativamente. Detalhes em
        // KeycloakRolesClaimsTransformation. Idempotente.
        services.AddTransient<IClaimsTransformation, KeycloakRolesClaimsTransformation>();
        // Variante required: lê do mesmo IUserContext mas falha-rápido em
        // request anônima. Use em handlers/controllers protegidos por
        // [Authorize] onde anônimo é estado impossível.
        services.AddScoped<IRequiredUserContext>(sp =>
            new RequiredUserContext(sp.GetRequiredService<IUserContext>()));
    }

    private static void AddVerificacaoDoProvedorDeIdentidade(IServiceCollection services)
    {
        services.AddHttpClient(nameof(OidcDiscoveryHealthCheck))
            .ConfigureHttpClient(static cliente => cliente.Timeout = OidcDiscoveryHealthCheck.LimiteDeEspera);
        services.AddHealthChecks()
            .AddCheck<OidcDiscoveryHealthCheck>(
                name: "oidc-discovery",
                failureStatus: HealthStatus.Degraded,
                tags: ["auth"]);
    }
}
