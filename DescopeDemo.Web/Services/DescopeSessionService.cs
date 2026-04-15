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

public sealed class DescopeSessionService : IDescopeSessionService
{
    private readonly IDescopeClient _client;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DescopeSessionService> _logger;

    public DescopeSessionService(
        IDescopeClient client,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DescopeSessionService> logger)
    {
        _client = client;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Token> ValidateSessionAsync(string sessionJwt)
    {
        return await _client.Auth.ValidateSessionAsync(sessionJwt);
    }

    public async Task<Token> RefreshSessionAsync(string refreshJwt)
    {
        return await _client.Auth.RefreshSessionAsync(refreshJwt);
    }

    /// <summary>
    /// Calls the Descope logout API to invalidate the refresh token server-side.
    /// Uses IHttpClientFactory to avoid socket exhaustion from repeated allocations.
    /// The Authorization header format is "Bearer {projectId}:{refreshToken}"
    /// as required by Descope's REST API.
    /// </summary>
    public async Task LogoutAsync(string refreshJwt)
    {
        var projectId = _configuration["Descope:ProjectId"] ?? "";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{projectId}:{refreshJwt}");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var httpClient = _httpClientFactory.CreateClient("DescopeManagement");
        var response = await httpClient.SendAsync(request);
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
