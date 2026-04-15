// =============================================================================
// DocumentsController.cs — Example of a protected page
// =============================================================================
// The [Authorize] attribute is all you need to protect any controller or action.
// ASP.NET Core's authentication middleware (configured in Program.cs) handles
// validating the Descope JWT automatically. If the token is missing or invalid,
// the user is redirected to /Auth/Login.
// =============================================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DescopeDemo.Web.Controllers;

[Authorize] // This single attribute protects the entire controller with Descope auth
public sealed class DocumentsController : Controller
{
    public IActionResult Index() => View();
}
