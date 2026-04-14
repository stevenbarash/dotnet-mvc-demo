// =============================================================================
// TenantApp.cs — Represents an SSO application configured for a Descope tenant
// =============================================================================
// Each tenant in Descope can have federated SSO apps (SAML or OIDC) that their
// users can access via IdP-initiated single sign-on. This record holds the
// details needed to render an app tile on the dashboard.
// =============================================================================

namespace DescopeDemo.Web.Models;

/// <summary>
/// An SSO application belonging to a Descope tenant. Rendered as a clickable
/// tile on the dashboard — clicking launches IdP-initiated SSO to the target app.
/// </summary>
/// <param name="Id">The Descope app ID (used internally by Descope).</param>
/// <param name="Name">Display name shown on the app tile.</param>
/// <param name="Logo">Optional logo URL for the app tile icon.</param>
/// <param name="IdpInitiatedUrl">The URL that starts the SSO flow when the user clicks the tile.</param>
/// <param name="Enabled">Whether the app is currently active in Descope.</param>
public sealed record TenantApp(
    string Id,
    string Name,
    string? Logo,
    string? IdpInitiatedUrl,
    bool Enabled);
