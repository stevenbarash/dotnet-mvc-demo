// =============================================================================
// DescopeServiceCollectionExtensions.cs — DI registration for Descope services
// =============================================================================
// Registers the named HttpClient for the Descope Management API and the
// DescopeAppService. Called from Program.cs as builder.Services.AddDescopeAppServices().
//
// Uses a named HttpClient ("DescopeManagement") so the base address is configured
// once and the HttpClientFactory handles connection pooling and lifetime.
// =============================================================================

namespace DescopeDemo.Web.Services;

public static class DescopeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Descope Management API HttpClient and app discovery service.
    /// Call this in Program.cs to enable the dashboard's tenant app tiles feature.
    /// </summary>
    public static IServiceCollection AddDescopeAppServices(this IServiceCollection services)
    {
        // Named HttpClient for the Descope Management API (https://api.descope.com).
        // IHttpClientFactory manages connection pooling and DNS rotation automatically.
        services.AddHttpClient("DescopeManagement", client =>
        {
            client.BaseAddress = new Uri("https://api.descope.com");
        });

        services.AddScoped<IDescopeAppService, DescopeAppService>();

        return services;
    }
}
