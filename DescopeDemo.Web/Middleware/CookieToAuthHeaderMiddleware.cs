// =============================================================================
// CookieToAuthHeaderMiddleware.cs — Bridges Descope cookies to Bearer tokens
// =============================================================================
// WHY THIS EXISTS:
// ASP.NET Core's JwtBearer authentication handler only reads tokens from the
// Authorization header. But browsers send tokens as cookies, not headers.
// This middleware bridges that gap by reading the Descope session cookie ("DS")
// and copying it into the Authorization header before the auth middleware runs.
//
// It also handles transparent session refresh: if the session token has expired
// but a valid refresh token ("DSR") exists, it calls the Descope SDK to get a
// fresh session token — the user never sees an interruption.
//
// COOKIE NAMES (Descope convention):
//   "DS"  = Descope Session  — short-lived JWT (~1 hour)
//   "DSR" = Descope Refresh  — long-lived refresh token (30 days)
//
// NOTE: This middleware is only used in JwtBearer mode. In DescopeSdk mode,
// the custom auth handler reads cookies directly (see DescopeSdkAuthHandler.cs).
// =============================================================================

using DescopeDemo.Web.Helpers;
using DescopeDemo.Web.Services;

namespace DescopeDemo.Web.Middleware;

public class CookieToAuthHeaderMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<CookieToAuthHeaderMiddleware> _logger;

    public CookieToAuthHeaderMiddleware(RequestDelegate next, IWebHostEnvironment environment, ILogger<CookieToAuthHeaderMiddleware> logger)
    {
        _next = next;
        _environment = environment;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only inject a Bearer token if one isn't already present (e.g., from
        // an API client that sends its own Authorization header).
        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            var sessionToken = context.Request.Cookies["DS"];
            var refreshToken = context.Request.Cookies["DSR"];

            // If the session cookie is missing/expired but we have a refresh
            // token, try to get a new session token from Descope automatically.
            // This keeps the user logged in without any visible re-authentication.
            if (string.IsNullOrEmpty(sessionToken) && !string.IsNullOrEmpty(refreshToken))
            {
                sessionToken = await TryRefreshAsync(context, refreshToken);
            }

            // Copy the session JWT into the Authorization header so the
            // JwtBearer handler can pick it up and validate it.
            if (!string.IsNullOrEmpty(sessionToken))
            {
                context.Request.Headers.Append("Authorization", $"Bearer {sessionToken}");
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Attempts to refresh an expired session using the Descope SDK.
    /// On success, updates the "DS" cookie with the new JWT so subsequent
    /// requests don't need to refresh again until this new token expires.
    /// </summary>
    private async Task<string?> TryRefreshAsync(HttpContext context, string refreshToken)
    {
        try
        {
            var sessionService = context.RequestServices.GetRequiredService<IDescopeSessionService>();
            var newToken = await sessionService.RefreshSessionAsync(refreshToken);
            var newJwt = newToken.Jwt;

            if (!string.IsNullOrEmpty(newJwt))
            {
                // Update the session cookie with the fresh JWT
                context.Response.Cookies.Append("DS", newJwt, DescopeCookieOptions.Create(_environment.IsDevelopment()));
                _logger.LogDebug("Session token refreshed successfully");
                return newJwt;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh session token");
        }

        return null;
    }
}
