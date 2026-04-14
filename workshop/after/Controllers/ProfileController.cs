using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DescopeWorkshop.Web.Controllers;

[Authorize]
public class ProfileController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _descopeProjectId;

    public ProfileController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _descopeProjectId = configuration["Descope:ProjectId"]!;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Name = User.FindFirst("sub")?.Value ?? "Unknown";
        ViewBag.Email = "N/A";

        // Descope's session JWT is minimal — fetch user details from the API.
        // Auth format: Bearer {projectId}:{refreshToken}
        var refreshToken = Request.Cookies["DSR"];
        if (!string.IsNullOrEmpty(refreshToken))
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    "https://api.descope.com/v1/auth/me");
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", $"{_descopeProjectId}:{refreshToken}");

                using var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(
                        await response.Content.ReadAsStreamAsync());

                    if (doc.RootElement.TryGetProperty("name", out var name))
                        ViewBag.Name = name.GetString() ?? ViewBag.Name;
                    if (doc.RootElement.TryGetProperty("email", out var email))
                        ViewBag.Email = email.GetString() ?? ViewBag.Email;
                }
            }
            catch
            {
                // Fall through to defaults set above
            }
        }

        return View();
    }
}
