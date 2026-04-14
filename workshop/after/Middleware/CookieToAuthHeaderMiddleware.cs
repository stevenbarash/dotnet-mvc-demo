using System.Text.Json;

namespace DescopeWorkshop.Web.Middleware;

public class CookieToAuthHeaderMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _descopeProjectId;

    public CookieToAuthHeaderMiddleware(
        RequestDelegate next, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _next = next;
        _httpClientFactory = httpClientFactory;
        _descopeProjectId = configuration["Descope:ProjectId"]!;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            var sessionToken = context.Request.Cookies["DS"];

            if (string.IsNullOrEmpty(sessionToken))
            {
                var refreshToken = context.Request.Cookies["DSR"];
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    sessionToken = await TryRefreshAsync(refreshToken, context);
                }
            }

            if (!string.IsNullOrEmpty(sessionToken))
            {
                context.Request.Headers.Append("Authorization", $"Bearer {sessionToken}");
            }
        }

        await _next(context);
    }

    private async Task<string?> TryRefreshAsync(string refreshToken, HttpContext context)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            // Descope auth format: Bearer {projectId}:{refreshToken}
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://api.descope.com/v1/auth/refresh");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                    $"{_descopeProjectId}:{refreshToken}");

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync());

            if (!doc.RootElement.TryGetProperty("sessionJwt", out var sessionProp))
                return null;

            var newSessionJwt = sessionProp.GetString();
            if (string.IsNullOrEmpty(newSessionJwt))
                return null;

            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            };

            context.Response.Cookies.Append("DS", newSessionJwt, cookieOptions);

            if (doc.RootElement.TryGetProperty("refreshJwt", out var refreshProp))
            {
                var newRefreshJwt = refreshProp.GetString();
                if (!string.IsNullOrEmpty(newRefreshJwt))
                    context.Response.Cookies.Append("DSR", newRefreshJwt, cookieOptions);
            }

            return newSessionJwt;
        }
        catch
        {
            return null;
        }
    }
}
