# Descope Authentication Workshop

Add authentication to a .NET MVC app in 15 minutes using [Descope](https://www.descope.com/).

**Audience:** .NET developers who want to see how Descope integrates with ASP.NET Core.

**What you'll do:** Start with an unprotected app (`before/`), add Descope authentication, and guard a profile page so only logged-in users can access it. The finished version is in `after/` for reference.

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A free [Descope account](https://www.descope.com/sign-up)

---

## Step 1: Run the "before" app

```bash
cd before
dotnet run
```

Open https://localhost:5001 in your browser. Click **View Profile** — notice anyone can access it. No authentication at all.

Stop the app with `Ctrl+C`.

---

## Step 2: Set up Descope

1. Sign in to the [Descope Console](https://app.descope.com)
2. Create a new project (or use an existing one)
3. Go to **Project Settings** and copy your **Project ID**
4. Your project includes a default flow with the ID `sign-up-or-in` — this is what the workshop code uses. You can preview and edit it under **Flows** in the console. To use a different flow, browse the [Flow Library](https://docs.descope.com/flows/intro-to-flows/flow-library) (click **Start from template**), pick a template, and replace `sign-up-or-in` in `Views/Auth/Login.cshtml` with your new flow's ID.

---

## Step 3: Add the JwtBearer NuGet package

```bash
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.14
```

---

## Step 4: Add Descope config to appsettings.json

Add this section to your `appsettings.json`:

```json
{
  "Descope": {
    "ProjectId": "YOUR_PROJECT_ID"
  }
}
```

Replace `YOUR_PROJECT_ID` with the Project ID you copied from the Descope Console.

---

## Step 5: Wire up authentication in Program.cs

Replace your `Program.cs` with:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

var descopeProjectId = builder.Configuration["Descope:ProjectId"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Authority enables OIDC discovery for signing keys.
        // ValidIssuer is set explicitly because Descope's JWT "iss" claim
        // differs from the discovery base URL.
        options.Authority = $"https://api.descope.com/{descopeProjectId}";

        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidIssuer = $"https://api.descope.com/v1/apps/{descopeProjectId}",
            ValidAudiences = [descopeProjectId],
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.Redirect("/Auth/Login");
                return Task.CompletedTask;
            }
        };
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Must come BEFORE UseAuthentication — reads the "DS" cookie
// and copies it into the Authorization header for JwtBearer.
app.UseMiddleware<DescopeWorkshop.Web.Middleware.CookieToAuthHeaderMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
```

---

## Step 6: Add the cookie-to-header middleware

Create `Middleware/CookieToAuthHeaderMiddleware.cs`:

```csharp
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
```

**Why is this needed?** ASP.NET Core's JwtBearer handler reads tokens from the `Authorization` header, but browsers send tokens as cookies. This middleware bridges the gap. It also handles **token refresh** — when the session token expires, it uses the refresh token to get a new one from Descope without forcing the user to log in again.

---

## Step 7: Add the Auth controller

Create `Controllers/AuthController.cs`:

```csharp
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
```

---

## Step 8: Add the Login view

Create `Views/Auth/Login.cshtml`:

```html
@{
    ViewData["Title"] = "Sign In";
}

<div class="row justify-content-center mt-5">
    <div class="col-md-5">
        <div class="card shadow">
            <div class="card-body p-4">
                <h2 class="card-title text-center mb-4">Sign In</h2>
                <div id="descope-login"></div>

                <form id="callback-form" method="post" action="/Auth/Callback" style="display:none;">
                    @Html.AntiForgeryToken()
                    <input type="hidden" name="sessionToken" id="sessionToken" />
                    <input type="hidden" name="refreshToken" id="refreshToken" />
                </form>
            </div>
        </div>
    </div>
</div>

@section Scripts {
    <script src="https://unpkg.com/@@descope/web-component@@latest/dist/index.js"></script>
    <script src="https://unpkg.com/@@descope/web-js-sdk@@latest/dist/index.umd.js"></script>
    <script>
        const wcElement = document.createElement('descope-wc');
        wcElement.setAttribute('project-id', '@ViewBag.ProjectId');
        wcElement.setAttribute('flow-id', 'sign-up-or-in');

        wcElement.addEventListener('success', (e) => {
            document.getElementById('sessionToken').value = e.detail?.sessionJwt || '';
            document.getElementById('refreshToken').value = e.detail?.refreshJwt || '';
            document.getElementById('callback-form').submit();
        });

        wcElement.addEventListener('error', (e) => {
            console.error('Descope error:', e.detail);
        });

        document.getElementById('descope-login').appendChild(wcElement);
    </script>
}
```

---

## Step 9: Protect the Profile page

Replace `Controllers/ProfileController.cs` with:

```csharp
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
```

**Why call `/v1/auth/me`?** Descope's session JWT is intentionally minimal (just `sub`, `aud`, `iss`, `exp`) for performance. User details like name and email must be fetched from Descope's API. The auth format is `Bearer {projectId}:{refreshToken}`.

Replace `Views/Profile/Index.cshtml` with:

```html
@{
    ViewData["Title"] = "My Profile";
}

<h2>My Profile</h2>
<div class="card mt-3" style="max-width: 400px;">
    <div class="card-body">
        <h5 class="card-title">@ViewBag.Name</h5>
        <p class="card-text"><strong>Email:</strong> @ViewBag.Email</p>
    </div>
</div>
<p class="text-success mt-3">You are authenticated. This page is protected by Descope.</p>
```

---

## Step 10: Update the Home page

Replace the message in `Views/Home/Index.cshtml` so it reflects the new auth setup:

```html
@{
    ViewData["Title"] = "Home";
}

<div class="text-center mt-5">
    <h1 class="display-5">Welcome to the Workshop App</h1>
    <p class="lead mt-3">This app is protected by Descope. Sign in to view your profile.</p>
    <a class="btn btn-primary btn-lg mt-3" asp-controller="Profile" asp-action="Index">View Profile</a>
</div>
```

---

## Step 11: Update the layout with Login/Logout links

Replace `Views/Shared/_Layout.cshtml` with:

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] - Workshop App</title>
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" />
    <link rel="stylesheet" href="~/css/site.css" />
</head>
<body>
    <nav class="navbar navbar-expand-lg navbar-dark bg-dark">
        <div class="container">
            <a class="navbar-brand" asp-controller="Home" asp-action="Index">Workshop App</a>
            <div class="navbar-nav ms-auto">
                <a class="nav-link" asp-controller="Home" asp-action="Index">Home</a>
                <a class="nav-link" asp-controller="Profile" asp-action="Index">Profile</a>
                @if (User.Identity?.IsAuthenticated == true)
                {
                    <span class="nav-link text-light">@(User.Identity.Name ?? "User")</span>
                    <form asp-controller="Auth" asp-action="Logout" method="post" class="d-inline">
                        <button type="submit" class="nav-link btn btn-link text-light">Logout</button>
                    </form>
                }
                else
                {
                    <a class="nav-link" asp-controller="Auth" asp-action="Login">Sign In</a>
                }
            </div>
        </div>
    </nav>
    <div class="container py-4">
        @RenderBody()
    </div>
    <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js"></script>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

---

## Step 12: Run and verify

```bash
dotnet run
```

Open https://localhost:5001 and run through the checklist:

- [ ] Click **Profile** — you should be redirected to **Sign In**
- [ ] Complete the Descope login flow — you should land on the Profile page with your real name and email
- [ ] Click **Logout** — you should be redirected to the Home page
- [ ] Click **Profile** again — you should be redirected back to **Sign In**

You did it! You added Descope authentication to a .NET app.

---

## What happened?

| Before | After |
|--------|-------|
| No authentication packages | `Microsoft.AspNetCore.Authentication.JwtBearer` |
| No middleware pipeline for auth | Cookie-to-header middleware with token refresh + `UseAuthentication` + `UseAuthorization` |
| Profile page open to everyone | `[Authorize]` attribute redirects to login |
| Hardcoded user data | Real user data fetched from Descope's `/v1/auth/me` API |
| No login/logout UI | Descope Web Component handles the entire auth flow |

---

## Stuck?

Compare your work against the `after/` folder. You can diff individual files:

```bash
diff before/Program.cs after/Program.cs
diff before/Controllers/ProfileController.cs after/Controllers/ProfileController.cs
```

Or use your editor's diff tool to compare the entire `before/` and `after/` directories.
