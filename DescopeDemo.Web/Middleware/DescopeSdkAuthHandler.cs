// =============================================================================
// DescopeSdkAuthHandler.cs — Alternative auth handler using the Descope SDK
// =============================================================================
// This is the "DescopeSdk" validation mode (the alternative to JwtBearer).
// Instead of using ASP.NET Core's built-in JWT validation, it calls the Descope
// SDK's ValidateSessionAsync() method directly.
//
// WHY USE THIS INSTEAD OF JWTBEARER?
//   - Access to Descope-specific token properties (permissions, step-up status)
//   - Consistent validation behavior with other Descope SDKs (React, Node, etc.)
//   - Simpler setup — no need for Authority/Issuer/Audience configuration
//
// HOW IT WORKS:
//   1. Reads the "DS" session cookie (no middleware needed — reads directly)
//   2. Calls _descopeClient.Auth.ValidateSessionAsync() to verify the JWT
//   3. Maps the token's claims into a standard ClaimsPrincipal
//   4. ASP.NET Core treats the user as authenticated for [Authorize] checks
// =============================================================================

using System.Security.Claims;
using System.Text.Encodings.Web;
using Descope;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DescopeDemo.Web.Middleware;

/// <summary>
/// Custom ASP.NET Core authentication handler that validates Descope session
/// tokens using the Descope .NET SDK. This plugs into the standard ASP.NET Core
/// authentication pipeline — controllers use [Authorize] as normal.
/// </summary>
public class DescopeSdkAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IDescopeClient _descopeClient;

    public DescopeSdkAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDescopeClient descopeClient)
        : base(options, logger, encoder)
    {
        _descopeClient = descopeClient;
    }

    /// <summary>
    /// Called on every request to a protected endpoint. Reads the Descope session
    /// cookie and validates it using the SDK. If valid, creates a ClaimsPrincipal
    /// with the user's claims so the rest of the app can use User.Identity,
    /// User.FindFirst("tenants"), etc.
    /// </summary>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Read the session JWT directly from the cookie — no middleware needed
        // in SDK mode (unlike JwtBearer mode which needs CookieToAuthHeaderMiddleware).
        var sessionJwt = Request.Cookies["DS"];
        if (string.IsNullOrEmpty(sessionJwt))
        {
            return AuthenticateResult.NoResult();
        }

        try
        {
            // The SDK validates the JWT signature, expiration, and issuer
            // using the Descope project's public keys (fetched automatically).
            var token = await _descopeClient.Auth.ValidateSessionAsync(sessionJwt);

            // Map Descope token claims to a standard .NET ClaimsPrincipal.
            // This makes Descope claims available via User.FindFirst("tenants"),
            // User.FindFirst("dct"), etc. — the same API you'd use with any
            // ASP.NET Core authentication provider.
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, token.Subject ?? ""),
            };

            if (token.Claims != null)
            {
                foreach (var kvp in token.Claims)
                {
                    var claimType = kvp.Key switch
                    {
                        "name" => ClaimTypes.Name,
                        "email" => ClaimTypes.Email,
                        _ => kvp.Key
                    };
                    claims.Add(new Claim(claimType, kvp.Value?.ToString() ?? ""));
                }
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
        catch (DescopeException ex)
        {
            Logger.LogWarning("Descope session validation failed: {Message}", ex.Message);
            return AuthenticateResult.Fail("Invalid session");
        }
    }

    /// <summary>
    /// When an unauthenticated user hits a protected page, redirect to the
    /// Descope login page instead of returning a raw 401 response.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/Auth/Login");
        return Task.CompletedTask;
    }
}
