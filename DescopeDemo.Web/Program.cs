// =============================================================================
// Program.cs — Application entry point & Descope authentication setup
// =============================================================================
// This file shows everything needed to integrate Descope into a .NET 8 MVC app.
// The key integration points are:
//   1. Register the Descope SDK client (for session validation & refresh)
//   2. Configure authentication (choose between JwtBearer or Descope SDK mode)
//   3. Add cookie-to-header middleware (bridges browser cookies to Bearer tokens)
// =============================================================================

using DescopeDemo.Web.Services;
using Descope;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// ---------------------------------------------------------------------------
// STEP 1: Register the Descope SDK client
// ---------------------------------------------------------------------------
// The Descope .NET SDK (NuGet: Descope) provides session validation, token
// refresh, and management APIs. We register it here so it can be injected
// anywhere in the app. The only required setting is your Project ID, which
// you can find in the Descope Console under Project Settings.
// ---------------------------------------------------------------------------
var descopeProjectId = builder.Configuration["Descope:ProjectId"]!;
builder.Services.AddDescopeClient(new DescopeClientOptions
{
    ProjectId = descopeProjectId
});

// Register our thin wrapper around the Descope SDK for session operations
// (validate, refresh, logout) and the app-discovery service that fetches
// tenant SSO apps from the Descope Management API.
builder.Services.AddScoped<DescopeDemo.Web.Services.IDescopeSessionService, DescopeDemo.Web.Services.DescopeSessionService>();
builder.Services.AddDescopeAppServices();

// ---------------------------------------------------------------------------
// STEP 2: Configure authentication — two modes to choose from
// ---------------------------------------------------------------------------
// This demo supports two validation strategies (set in appsettings.json):
//
//   "JwtBearer"   — Standard ASP.NET Core JWT Bearer authentication.
//                    Descope issues standard JWTs, so this works out of the box
//                    with Microsoft's built-in middleware. No Descope SDK needed
//                    at validation time — just configure issuer & audience.
//
//   "DescopeSdk"  — Uses the Descope SDK's ValidateSessionAsync() directly via
//                    a custom AuthenticationHandler. Useful if you want to use
//                    Descope-specific features like permissions or step-up auth.
//
// Both modes read the session JWT from the "DS" cookie. The JwtBearer mode
// relies on CookieToAuthHeaderMiddleware to copy the cookie into a Bearer
// header (since JwtBearer middleware only reads the Authorization header).
// ---------------------------------------------------------------------------
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
