// =============================================================================
// AuthController.cs — Handles login, callback, and logout with Descope
// =============================================================================
// This controller manages the authentication lifecycle:
//
//   1. Login   — Renders the Descope Web Component (<descope-wc>) which handles
//                the entire login UI (passwords, SSO, MFA, etc.) via Descope Flows.
//   2. Callback — Receives the session & refresh JWTs after successful login and
//                stores them as HttpOnly cookies for subsequent requests.
//   3. Logout  — Invalidates the Descope session server-side and clears cookies.
//
// This replaces what would normally be dozens of lines of custom login/password
// validation code. Descope Flows handle all the auth UI and logic — the server
// just needs to store and validate the resulting JWTs.
// =============================================================================

using DescopeDemo.Web.Helpers;
using DescopeDemo.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DescopeDemo.Web.Controllers;

public sealed class AuthController : Controller
{
    private readonly IDescopeSessionService _sessionService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public AuthController(IDescopeSessionService sessionService, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _sessionService = sessionService;
        _configuration = configuration;
        _environment = environment;
    }

    /// <summary>
    /// Renders the login page with the Descope Web Component.
    /// The Web Component handles the entire authentication UI — no custom login
    /// form markup needed. Just drop in <descope-wc> with your Project ID and
    /// Flow ID, and Descope handles passwords, SSO, MFA, etc.
    /// </summary>
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        // Already authenticated? Skip straight to dashboard.
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");

        // Pass the Project ID to the view so the Descope Web Component knows
        // which Descope project to authenticate against.
        ViewBag.ProjectId = _configuration["Descope:ProjectId"];
        ViewBag.ReturnUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/Dashboard";
        return View();
    }

    /// <summary>
    /// Called after a successful Descope Flow. The Descope Web Component fires a
    /// "success" event containing sessionJwt and refreshJwt. The Login view posts
    /// these tokens here via a hidden form.
    ///
    /// We store them as HttpOnly cookies:
    ///   - "DS"  = session token  (short-lived, ~1 hour)
    ///   - "DSR" = refresh token  (long-lived, 30 days — used to get new session tokens)
    ///
    /// On subsequent requests, the CookieToAuthHeaderMiddleware reads "DS" and
    /// sets it as a Bearer token so ASP.NET Core's JwtBearer handler can validate it.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Callback([FromForm] string sessionToken, [FromForm] string refreshToken, [FromForm] string? returnUrl)
    {
        if (string.IsNullOrEmpty(sessionToken))
            return BadRequest("Session token is required");

        var isDev = _environment.IsDevelopment();

        // Store the session JWT — this is the short-lived token validated on
        // every request. HttpOnly prevents JavaScript access (XSS protection).
        Response.Cookies.Append("DS", sessionToken, DescopeCookieOptions.Create(isDev));

        // Store the refresh JWT — used to silently obtain new session tokens
        // when the session expires, avoiding forcing the user to log in again.
        if (!string.IsNullOrEmpty(refreshToken))
        {
            Response.Cookies.Append("DSR", refreshToken, DescopeCookieOptions.Create(isDev, TimeSpan.FromDays(30)));
        }

        var safeUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/Dashboard";
        return Redirect(safeUrl);
    }

    /// <summary>
    /// Logs the user out by:
    ///   1. Calling Descope's logout API to invalidate the refresh token server-side
    ///   2. Deleting both cookies from the browser
    /// The server-side logout ensures the refresh token can't be reused even if
    /// someone captured it.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var refreshToken = Request.Cookies["DSR"];
        if (!string.IsNullOrEmpty(refreshToken))
        {
            // Best effort — we clear cookies regardless of whether the API call succeeds,
            // so the user is always logged out locally even if the network is down.
            try { await _sessionService.LogoutAsync(refreshToken); }
            catch { /* Best effort — clear cookies regardless */ }
        }
        Response.Cookies.Delete("DS");
        Response.Cookies.Delete("DSR");
        return RedirectToAction("Index", "Home");
    }
}
