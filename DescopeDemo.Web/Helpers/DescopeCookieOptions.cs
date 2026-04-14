// =============================================================================
// DescopeCookieOptions.cs — Shared cookie configuration for Descope tokens
// =============================================================================
// Centralizes the HttpOnly cookie settings used when storing Descope session
// and refresh tokens. Used by AuthController (login callback) and
// CookieToAuthHeaderMiddleware (silent token refresh).
// =============================================================================

namespace DescopeDemo.Web.Helpers;

internal static class DescopeCookieOptions
{
    /// <summary>
    /// Creates cookie options for storing a Descope token. All Descope cookies
    /// are HttpOnly (XSS protection), Secure in production, and SameSite Strict.
    /// </summary>
    /// <param name="isDevelopment">True in development (uses Lax SameSite, no Secure flag for localhost).</param>
    /// <param name="expiry">Cookie lifetime. Defaults to 1 hour (session token). Use 30 days for refresh tokens.</param>
    public static CookieOptions Create(bool isDevelopment, TimeSpan? expiry = null) => new()
    {
        HttpOnly = true,
        Secure = !isDevelopment,
        SameSite = isDevelopment ? SameSiteMode.Lax : SameSiteMode.Strict,
        Path = "/",
        Expires = DateTimeOffset.UtcNow.Add(expiry ?? TimeSpan.FromHours(1))
    };
}
