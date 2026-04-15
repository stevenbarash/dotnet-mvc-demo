// =============================================================================
// Program.cs — Application entry point & Descope authentication setup
// =============================================================================
// This file shows everything needed to integrate Descope into a .NET 8 MVC app.
// The key integration points are:
//   1. Register Descope services (SDK client, session, Management API)
//   2. Configure authentication (choose between JwtBearer or Descope SDK mode)
//   3. Add cookie-to-header middleware (bridges browser cookies to Bearer tokens)
// =============================================================================

using DescopeDemo.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// ---------------------------------------------------------------------------
// STEP 1: Register all Descope services
// ---------------------------------------------------------------------------
// AddDescopeServices registers:
//   - Strongly-typed DescopeOptions with startup validation (fail fast)
//   - The Descope SDK client for session validation & refresh
//   - An IHttpClientFactory-managed named client for the Management API
//   - Session and app discovery services
// ---------------------------------------------------------------------------
builder.Services.AddDescopeServices(builder.Configuration);

var descopeProjectId = builder.Configuration["Descope:ProjectId"] ?? "";
var validationMode = builder.Configuration["Authentication:ValidationMode"] ?? "JwtBearer";

if (validationMode == "JwtBearer")
{
    // --- Option A: JwtBearer (recommended for most apps) ---
    // Descope's OIDC-compliant token endpoint serves JWKS keys automatically,
    // so ASP.NET Core can validate tokens without any Descope-specific code.
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            // Authority tells the middleware where to fetch the OIDC discovery
            // document and signing keys (/.well-known/openid-configuration).
            options.Authority = $"https://api.descope.com/{descopeProjectId}";

            // Standard JWT validation: verify the issuer, audience, signature,
            // and that the token hasn't expired.
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
                // Log authentication failures for debugging
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
                    logger.LogError(context.Exception, "JWT authentication failed");
                    return Task.CompletedTask;
                },

                // Diagnostic logging to trace token flow through the pipeline
                OnMessageReceived = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
                    var hasToken = !string.IsNullOrEmpty(context.Token);
                    var hasCookie = context.Request.Cookies.ContainsKey("DS");
                    var hasAuthHeader = context.Request.Headers.ContainsKey("Authorization");
                    logger.LogDebug("JWT OnMessageReceived: HasToken={HasToken}, HasCookie={HasCookie}, HasAuthHeader={HasAuthHeader}", hasToken, hasCookie, hasAuthHeader);
                    return Task.CompletedTask;
                },

                // When an unauthenticated user hits a protected page, redirect
                // them to our login page instead of returning a raw 401.
                OnChallenge = context =>
                {
                    context.HandleResponse();
                    var returnUrl = context.Request.Path + context.Request.QueryString;
                    context.Response.Redirect($"/Auth/Login?returnUrl={Uri.EscapeDataString(returnUrl)}");
                    return Task.CompletedTask;
                }
            };
        });
}
else
{
    // --- Option B: Descope SDK validation ---
    // Registers a custom AuthenticationHandler that calls the Descope SDK
    // directly. See Middleware/DescopeSdkAuthHandler.cs for implementation.
    builder.Services.AddAuthentication("DescopeSdk")
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DescopeDemo.Web.Middleware.DescopeSdkAuthHandler>("DescopeSdk", null);
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseStaticFiles();
app.UseRouting();

// ---------------------------------------------------------------------------
// STEP 3: Wire up the middleware pipeline
// ---------------------------------------------------------------------------
// In JwtBearer mode, we insert CookieToAuthHeaderMiddleware BEFORE the
// authentication middleware. This reads the "DS" session cookie and copies
// it into the Authorization header as a Bearer token, because ASP.NET Core's
// JwtBearer handler only looks at the Authorization header by default.
//
// If the session cookie has expired but a refresh cookie ("DSR") exists,
// the middleware automatically refreshes the session via the Descope SDK
// and updates the cookie — transparent to the user.
// ---------------------------------------------------------------------------
if (validationMode == "JwtBearer")
{
    app.UseMiddleware<DescopeDemo.Web.Middleware.CookieToAuthHeaderMiddleware>();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
