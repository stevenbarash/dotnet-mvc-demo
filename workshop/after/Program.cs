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
