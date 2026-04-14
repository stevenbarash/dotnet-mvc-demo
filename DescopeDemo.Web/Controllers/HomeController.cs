// =============================================================================
// HomeController.cs — Landing page with auto-redirect for authenticated users
// =============================================================================

using Microsoft.AspNetCore.Mvc;

namespace DescopeDemo.Web.Controllers;

public class HomeController : Controller
{
    /// <summary>
    /// Shows the public landing page. If the user already has a valid Descope
    /// session (their "DS" cookie contains a valid JWT), they're redirected
    /// straight to the dashboard — no need to see the marketing page again.
    /// </summary>
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");
        return View();
    }
}
