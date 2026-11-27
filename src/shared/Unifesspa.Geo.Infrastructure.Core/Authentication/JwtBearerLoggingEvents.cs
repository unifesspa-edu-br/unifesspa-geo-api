namespace Unifesspa.Geo.Infrastructure.Core.Authentication;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;

/// <summary>
/// Emits structured warnings for 401 flows in the JWT Bearer pipeline so operators
/// can diagnose authentication failures by correlation id instead of blind 401s.
/// </summary>
internal static partial class JwtBearerLoggingEvents
{
    /// <summary>
    /// Wires <see cref="JwtBearerEvents.OnAuthenticationFailed"/> and
    /// <see cref="JwtBearerEvents.OnChallenge"/> into the provided <paramref name="events"/>.
    /// Existing handlers are chained — this method composes, does not replace.
    /// </summary>
    public static JwtBearerEvents WithStructuredLogging(this JwtBearerEvents events, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(logger);

        events.OnAuthenticationFailed = RegistrarFalha(events.OnAuthenticationFailed, logger);
        events.OnChallenge = RegistrarDesafio(events.OnChallenge, logger);

        return events;
    }

    private static Func<AuthenticationFailedContext, Task> RegistrarFalha(
        Func<AuthenticationFailedContext, Task> anterior,
        ILogger logger) =>
        async context =>
        {
            LogAuthenticationFailed(logger, context.Exception.GetType().Name, context.Exception);
            await anterior(context).ConfigureAwait(false);
        };

    private static Func<JwtBearerChallengeContext, Task> RegistrarDesafio(
        Func<JwtBearerChallengeContext, Task> anterior,
        ILogger logger) =>
        async context =>
        {
            LogChallenge(logger, context.Error ?? "no-error", context.ErrorDescription ?? "no-description");
            await anterior(context).ConfigureAwait(false);
        };

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "JWT authentication failed: {ExceptionType}")]
    private static partial void LogAuthenticationFailed(ILogger logger, string exceptionType, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "JWT challenge emitted: {Error} — {ErrorDescription}")]
    private static partial void LogChallenge(ILogger logger, string error, string errorDescription);
}
