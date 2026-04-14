using Microsoft.AspNetCore.Mvc;

namespace DescopeWorkshop.Web.Controllers;

public class AuthController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public AuthController(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Profile");

        ViewBag.ProjectId = _configuration["Descope:ProjectId"];
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Callback([FromForm] string sessionToken, [FromForm] string? refreshToken)
    {
        if (string.IsNullOrEmpty(sessionToken))
            return BadRequest("Session token is required");

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = "/"
        };

        Response.Cookies.Append("DS", sessionToken, cookieOptions);

        if (!string.IsNullOrEmpty(refreshToken))
            Response.Cookies.Append("DSR", refreshToken, cookieOptions);

        return RedirectToAction("Index", "Profile");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("DS");
        Response.Cookies.Delete("DSR");
        return RedirectToAction("Index", "Home");
    }
}
