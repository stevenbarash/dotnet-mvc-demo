using DescopeDemo.Web.Models;
using Descope;

namespace DescopeDemo.Web.Services;

public static class DescopeServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Descope services: SDK client, session management, Management
    /// API HttpClient, and app discovery. Binds and validates configuration on startup.
    /// </summary>
    public static IServiceCollection AddDescopeServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DescopeOptions>()
            .BindConfiguration(DescopeOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AuthenticationOptions>()
            .BindConfiguration(AuthenticationOptions.SectionName);

        var descopeProjectId = configuration[DescopeOptions.SectionName + ":ProjectId"] ?? "";

        services.AddDescopeClient(new DescopeClientOptions
        {
            ProjectId = descopeProjectId
        });

        services.AddHttpClient("DescopeManagement", client =>
        {
            client.BaseAddress = new Uri("https://api.descope.com");
        });

        services.AddScoped<IDescopeSessionService, DescopeSessionService>();
        services.AddScoped<IDescopeAppService, DescopeAppService>();

        return services;
    }
}
