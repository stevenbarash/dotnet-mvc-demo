// =============================================================================
// SettingsController.cs — Displays current Descope configuration for debugging
// =============================================================================
// This page is helpful during development to verify which Descope project and
// validation mode are active. It also shows what the old auth system looked like
// (Azure AD / local passwords) vs. the new Descope integration.
// =============================================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DescopeDemo.Web.Controllers;

[Authorize]
public sealed class SettingsController : Controller
{
    private readonly IConfiguration _configuration;
    public SettingsController(IConfiguration configuration) { _configuration = configuration; }

    /// <summary>
    /// Shows the active Descope configuration: Project ID, validation mode,
    /// and OIDC authority URL. Useful for verifying your setup is correct.
    /// </summary>
    public IActionResult Index()
    {
        ViewBag.ProjectId = _configuration["Descope:ProjectId"];
        ViewBag.ValidationMode = _configuration["Authentication:ValidationMode"] ?? "JwtBearer";
        ViewBag.Authority = _configuration["Authentication:Authority"];
        return View();
    }
}
