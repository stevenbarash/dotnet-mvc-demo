// =============================================================================
// DescopeAppService.cs — Fetches tenant SSO apps from Descope Management API
// =============================================================================
// This service uses Descope's Management API to discover which SSO applications
// (SAML or OIDC) are configured for a given tenant. The results are displayed
// as clickable app tiles on the dashboard, enabling IdP-initiated single sign-on.
//
// HOW IT WORKS:
//   1. Load the tenant from Descope to get its list of federated app IDs
//   2. Load each app's details (name, logo, SSO settings) in parallel
//   3. Extract the IdP-initiated URL so users can click to launch each app
//
// AUTHENTICATION:
//   The Management API requires a Management Key (not a user session token).
//   The Authorization header format is "Bearer {projectId}:{managementKey}".
//   Generate a Management Key in Descope Console → Company → Management Keys.
//
// NOTE: This is an optional feature. Basic Descope auth works without the
// Management API — you only need it if you want to programmatically discover
// tenant apps, manage users, or perform other admin operations.
// =============================================================================

using System.Text.Json;
using DescopeDemo.Web.Models;

namespace DescopeDemo.Web.Services;

public interface IDescopeAppService
{
    /// <summary>
    /// Fetches the SSO applications configured for a tenant in Descope.
    /// Returns app details including IdP-initiated SSO URLs for each app.
    /// </summary>
    Task<IReadOnlyList<TenantApp>> GetTenantAppsAsync(string tenantId, CancellationToken cancellationToken = default);
}

public sealed class DescopeAppService : IDescopeAppService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DescopeAppService> _logger;

    public DescopeAppService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<DescopeAppService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TenantApp>> GetTenantAppsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("DescopeManagement");
        var projectId = _configuration["Descope:ProjectId"] ?? "";
        var managementKey = _configuration["Descope:ManagementKey"] ?? "";

        // Descope Management API auth format: "Bearer {projectId}:{managementKey}"
        var authHeaderValue = $"Bearer {projectId}:{managementKey}";

        // Step 1: Load the tenant to get its federated app IDs.
        // Each tenant in Descope can have multiple SSO apps (SAML/OIDC)
        // associated with it via the "federatedAppIds" array.
        using var tenantRequest = new HttpRequestMessage(HttpMethod.Get, $"/v1/mgmt/tenant?id={Uri.EscapeDataString(tenantId)}");
        tenantRequest.Headers.TryAddWithoutValidation("Authorization", authHeaderValue);
        var tenantResponse = await client.SendAsync(tenantRequest, cancellationToken);
        if (!tenantResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to load tenant {TenantId}: {Status}", tenantId, tenantResponse.StatusCode);
            return [];
        }

        var tenantJson = await tenantResponse.Content.ReadAsStringAsync(cancellationToken);

        // Parse the "federatedAppIds" array from the tenant response
        var appIds = new List<string>();
        using (var tenantDoc = JsonDocument.Parse(tenantJson))
        {
            if (tenantDoc.RootElement.TryGetProperty("federatedAppIds", out var appIdsEl)
                && appIdsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in appIdsEl.EnumerateArray())
                {
                    var val = id.GetString();
                    if (!string.IsNullOrEmpty(val))
                        appIds.Add(val);
                }
            }
        }

        if (appIds.Count == 0)
            return [];

        // Step 2: Load each app's details in parallel for better performance.
        // Each call fetches the app's name, logo, enabled status, and SSO settings.
        var apps = new List<TenantApp>();
        var tasks = appIds.Select(appId => LoadAppAsync(client, projectId, appId, authHeaderValue, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks);

        foreach (var app in results)
        {
            if (app != null)
                apps.Add(app);
        }

        return apps;
    }

    /// <summary>
    /// Loads a single SSO app's details from the Descope Management API and
    /// extracts the IdP-initiated URL. Supports both SAML and OIDC apps.
    /// </summary>
    private async Task<TenantApp?> LoadAppAsync(HttpClient client, string projectId, string appId, string authHeaderValue, CancellationToken cancellationToken)
    {
        using var appRequest = new HttpRequestMessage(HttpMethod.Get, $"/v1/mgmt/sso/idp/app/load?id={Uri.EscapeDataString(appId)}");
        appRequest.Headers.TryAddWithoutValidation("Authorization", authHeaderValue);
        var appResponse = await client.SendAsync(appRequest, cancellationToken);
        if (!appResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to load app {AppId}: {Status}", appId, appResponse.StatusCode);
            return null;
        }

        var appJson = await appResponse.Content.ReadAsStringAsync(cancellationToken);
        using var appDoc = JsonDocument.Parse(appJson);
        var appEl = appDoc.RootElement;

        // Skip disabled apps
        var enabled = appEl.TryGetProperty("enabled", out var enabledEl) && enabledEl.GetBoolean();
        if (!enabled)
            return null;

        var name = appEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
        var logo = appEl.TryGetProperty("logo", out var logoEl) ? logoEl.GetString() : null;

        // Extract the IdP-initiated SSO URL based on whether this is a SAML or
        // OIDC app. IdP-initiated SSO lets users click a tile and go directly to
        // the target app without visiting the app's login page first.
        //
        // For SAML apps: use the configured idpInitiatedUrl, or construct the
        //   default Descope SAML initiation URL from the project and app IDs.
        // For OIDC apps: use the customIdpInitiatedLoginPageUrl if configured.
        string? idpInitiatedUrl = null;
        if (appEl.TryGetProperty("samlSettings", out var samlEl))
        {
            var url = samlEl.TryGetProperty("idpInitiatedUrl", out var samlUrlEl) ? samlUrlEl.GetString() : null;
            if (!string.IsNullOrEmpty(url))
                idpInitiatedUrl = url;
            else
                idpInitiatedUrl = $"https://api.descope.com/v1/auth/saml/idp/initiate?app={projectId}-{Uri.EscapeDataString(appId)}";
        }
        else if (appEl.TryGetProperty("oidcSettings", out var oidcEl)
                 && oidcEl.TryGetProperty("customIdpInitiatedLoginPageUrl", out var oidcUrlEl))
        {
            idpInitiatedUrl = oidcUrlEl.GetString();
        }

        return new TenantApp(
            Id: appId,
            Name: name,
            Logo: string.IsNullOrEmpty(logo) ? null : logo,
            IdpInitiatedUrl: idpInitiatedUrl,
            Enabled: true);
    }
}
