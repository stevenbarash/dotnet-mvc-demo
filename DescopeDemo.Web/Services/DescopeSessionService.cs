// =============================================================================
// DescopeSessionService.cs — Thin wrapper around the Descope SDK for sessions
// =============================================================================
// Provides three core session operations using the Descope .NET SDK:
//
//   ValidateSessionAsync — Verifies a session JWT is valid (signature + expiry)
//   RefreshSessionAsync  — Exchanges a refresh token for a new session JWT
//   LogoutAsync          — Invalidates a refresh token server-side
//
// The Descope SDK handles all the cryptographic validation internally (fetching
// JWKS keys, verifying signatures, checking expiration). You don't need to
// implement any of that yourself.
// =============================================================================

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Descope;

namespace DescopeDemo.Web.Services;

public interface IDescopeSessionService
{
    /// <summary>Validate a Descope session JWT (checks signature and expiry).</summary>
    Task<Token> ValidateSessionAsync(string sessionJwt);

    /// <summary>Exchange a refresh token for a new session JWT.</summary>
    Task<Token> RefreshSessionAsync(string refreshJwt);

    /// <summary>Invalidate a refresh token on Descope's server (server-side logout).</summary>
    Task LogoutAsync(string refreshJwt);
}

public class DescopeSessionService : IDescopeSessionService
{
    private readonly IDescopeClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DescopeSessionService> _logger;

    public DescopeSessionService(IDescopeClient client, IConfiguration configuration, ILogger<DescopeSessionService> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Validates a session JWT using the Descope SDK. The SDK automatically
    /// fetches and caches the project's public keys for signature verification.
    /// </summary>
    public async Task<Token> ValidateSessionAsync(string sessionJwt)
    {
        return await _client.Auth.ValidateSessionAsync(sessionJwt);
    }

    /// <summary>
    /// Exchanges a refresh token for a new session JWT. Called by
    /// CookieToAuthHeaderMiddleware when the session cookie has expired but the
    /// refresh cookie is still valid — keeps the user logged in seamlessly.
    /// </summary>
    public async Task<Token> RefreshSessionAsync(string refreshJwt)
    {
        return await _client.Auth.RefreshSessionAsync(refreshJwt);
    }

    /// <summary>
    /// Calls the Descope logout API to invalidate the refresh token server-side.
    /// This ensures the token can't be reused even if someone captured it.
    /// The Authorization header format is "Bearer {projectId}:{refreshToken}"
    /// as required by Descope's REST API.
    /// </summary>
    public async Task LogoutAsync(string refreshJwt)
    {
        var projectId = _configuration["Descope:ProjectId"] ?? "";
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", $"{projectId}:{refreshJwt}");
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("https://api.descope.com/v1/auth/logout", content);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("User logged out successfully");
        }
        else
        {
            _logger.LogWarning("Logout request failed: {Status}", response.StatusCode);
        }
    }
}
