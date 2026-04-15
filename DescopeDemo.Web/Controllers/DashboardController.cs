// =============================================================================
// DashboardController.cs — Authenticated dashboard showing tenant SSO apps
// =============================================================================
// After a user logs in via Descope, their JWT contains tenant information as
// claims. This controller reads those claims to determine which tenant the user
// belongs to, then fetches that tenant's SSO applications from Descope's
// Management API to render as clickable app tiles (IdP-initiated SSO).
//
// This demonstrates how Descope's multi-tenant model works:
//   - Users are assigned to tenants in the Descope Console
//   - Tenants have federated SSO apps (SAML/OIDC) configured
//   - The JWT includes tenant info as claims ("dct" and "tenants")
//   - Your app reads these claims to provide tenant-specific experiences
// =============================================================================

using System.Text.Json;
using DescopeDemo.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DescopeDemo.Web.Controllers;

[Authorize] // Requires a valid Descope session — unauthenticated users are redirected to /Auth/Login
public sealed class DashboardController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IDescopeAppService _appService;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(IConfiguration configuration, IDescopeAppService appService, ILogger<DashboardController> logger)
    {
        _configuration = configuration;
        _appService = appService;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewBag.ValidationMode = _configuration["Authentication:ValidationMode"] ?? "JwtBearer";

        // --- Extract the tenant ID from Descope JWT claims ---
        // Descope includes tenant info in two possible claim formats:
        //   "dct"     = the current/selected tenant ID (simple string, set when
        //               the user authenticates in a tenant context)
        //   "tenants" = JSON object of all tenants the user belongs to, e.g.
        //               {"T2abc": {"roles": [...]}, "T2def": {"roles": [...]}}
        var tenantId = User.FindFirst("dct")?.Value;

        // If "dct" isn't present, fall back to parsing the "tenants" claim
        // and using the first tenant ID found.
        if (string.IsNullOrEmpty(tenantId))
        {
            var tenantsJson = User.FindFirst("tenants")?.Value;
            if (!string.IsNullOrEmpty(tenantsJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(tenantsJson);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        tenantId = prop.Name;
                        break; // Use the first tenant
                    }
                }
                catch (JsonException)
                {
                    _logger.LogWarning("Failed to parse tenants claim");
                }
            }
        }

        if (string.IsNullOrEmpty(tenantId))
        {
            ViewBag.AppError = "No tenant associated with your account";
            ViewBag.Apps = Array.Empty<Models.TenantApp>();
            return View();
        }

        // Fetch the tenant's SSO apps from Descope's Management API.
        // These are rendered as clickable tiles with IdP-initiated SSO URLs.
        try
        {
            ViewBag.Apps = await _appService.GetTenantAppsAsync(tenantId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load apps for tenant {TenantId}", tenantId);
            ViewBag.AppError = "Unable to load applications";
            ViewBag.Apps = Array.Empty<Models.TenantApp>();
        }

        return View();
    }
}
