# Descope Demo — Descope Auth Demo

A .NET 8 MVC demo showing how easy it is to integrate [Descope](https://descope.com) into a standard ASP.NET Core application. Replaces both Azure AD OAuth and legacy local password authentication with Descope as the sole auth provider.

The codebase is thoroughly commented — every file explains what it does, why it was added, and how it connects to Descope. Read the source to learn the integration pattern.

## What's Included

| Project | Description |
|---------|-------------|
| `DescopeDemo.Web` | .NET 8 MVC app with Descope Flows authentication, tenant SSO app tiles, and dual validation modes |
| `DescopeDemo.Migration` | Console app for importing legacy users with hashed passwords (bcrypt/PBKDF2/argon2) into Descope |
| `DescopeDemo.Tests` | Unit tests for the Descope app service |

## Quick Start

1. Get a [Descope Project ID](https://app.descope.com/settings/project) and optionally a [Management Key](https://app.descope.com/settings/company/managementkeys) (only needed for tenant app tiles)
2. Store your secrets using .NET User Secrets (never commit real keys to `appsettings.json`):

```bash
cd DescopeDemo.Web
dotnet user-secrets init
dotnet user-secrets set "Descope:ProjectId" "<your-project-id>"
dotnet user-secrets set "Descope:ManagementKey" "<your-management-key>"
```

3. Run:

```bash
dotnet run --project DescopeDemo.Web
```

See [docs/workshop-guide.md](docs/workshop-guide.md) for the full walkthrough.

## How the Descope Integration Works

The entire integration touches just a few files. Here's the architecture at a glance:

```
Browser                         ASP.NET Core                        Descope
───────                         ────────────                        ───────
  │                                  │                                 │
  │  1. Load login page              │                                 │
  │─────────────────────────────────>│                                 │
  │  2. Render <descope-wc>          │                                 │
  │<─────────────────────────────────│                                 │
  │                                  │                                 │
  │  3. User authenticates           │                                 │
  │──────────────────────────────────────────────────────────────────>│
  │  4. Descope returns JWTs         │                                 │
  │<─────────────────────────────────────────────────────────────────│
  │                                  │                                 │
  │  5. POST tokens to /Auth/Callback│                                 │
  │─────────────────────────────────>│                                 │
  │  6. Store as HttpOnly cookies    │                                 │
  │<─────────────────────────────────│                                 │
  │                                  │                                 │
  │  7. Request protected page       │                                 │
  │─────────────────────────────────>│                                 │
  │  8. Middleware copies cookie     │                                 │
  │  ·  to Bearer header, JWT        │                                 │
  │  ·  middleware validates          │  (fetches JWKS keys)           │
  │  ·                               │────────────────────────────────>│
  │  9. Authenticated response       │                                 │
  │<─────────────────────────────────│                                 │
```

### Key Files

| File | Purpose |
|------|---------|
| [`Program.cs`](DescopeDemo.Web/Program.cs) | Configures JWT or SDK auth, wires up middleware pipeline |
| [`Services/DescopeServiceCollectionExtensions.cs`](DescopeDemo.Web/Services/DescopeServiceCollectionExtensions.cs) | `AddDescopeServices()` — registers all Descope services with validated config |
| [`Models/DescopeOptions.cs`](DescopeDemo.Web/Models/DescopeOptions.cs) | Strongly-typed Descope configuration with startup validation |
| [`Views/Auth/Login.cshtml`](DescopeDemo.Web/Views/Auth/Login.cshtml) | Embeds the `<descope-wc>` Web Component — replaces all custom login UI |
| [`Controllers/AuthController.cs`](DescopeDemo.Web/Controllers/AuthController.cs) | Handles login page, token callback (stores cookies), and logout |
| [`Middleware/CookieToAuthHeaderMiddleware.cs`](DescopeDemo.Web/Middleware/CookieToAuthHeaderMiddleware.cs) | Bridges browser cookies to Bearer tokens for JwtBearer validation; handles silent token refresh |
| [`Middleware/DescopeSdkAuthHandler.cs`](DescopeDemo.Web/Middleware/DescopeSdkAuthHandler.cs) | Alternative auth handler using the Descope SDK directly instead of JwtBearer |
| [`Services/DescopeSessionService.cs`](DescopeDemo.Web/Services/DescopeSessionService.cs) | Wraps the Descope SDK for session validation, refresh, and logout |
| [`Services/DescopeAppService.cs`](DescopeDemo.Web/Services/DescopeAppService.cs) | Fetches tenant SSO apps from the Descope Management API |
| [`appsettings.json`](DescopeDemo.Web/appsettings.json) | `Descope:ProjectId`, and `Authentication:ValidationMode` (`"JwtBearer"` or `"DescopeSdk"`) to toggle between Microsoft's JWT libraries and Descope's SDK |

### Two Integration Options — Choose Your Validation Mode

This demo ships with **two fully independent ways** to validate Descope session tokens, controlled by a single config toggle in `appsettings.json`:

```json
{
  "Authentication": {
    "ValidationMode": "JwtBearer"   // or "DescopeSdk"
  }
}
```

Change the value and restart the app — no code changes required. The Dashboard page shows which mode is active.

#### Option 1: `JwtBearer` — Microsoft's Built-in Libraries (default)

Uses [`Microsoft.AspNetCore.Authentication.JwtBearer`](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer) — the same middleware you'd use with any OIDC provider. Descope issues standard, OIDC-compliant JWTs, so ASP.NET Core validates them natively via JWKS discovery. No Descope-specific code runs at validation time.

- **How it works:** `CookieToAuthHeaderMiddleware` copies the `DS` session cookie into the `Authorization: Bearer` header, then ASP.NET Core's JwtBearer handler validates signature, issuer, audience, and expiration using keys fetched from Descope's OIDC discovery endpoint.
- **Best for:** Teams migrating existing .NET apps to Descope, microservices that already use JwtBearer, or environments where you want zero vendor-specific dependencies in your auth pipeline.

#### Option 2: `DescopeSdk` — Descope's .NET SDK

Uses the [Descope .NET SDK](https://www.nuget.org/packages/Descope) (`ValidateSessionAsync()`) via a custom `AuthenticationHandler`. The handler reads the `DS` cookie directly — no cookie-to-header middleware needed.

- **How it works:** `DescopeSdkAuthHandler` reads the session cookie, calls `_descopeClient.Auth.ValidateSessionAsync(sessionJwt)`, and maps the Descope token claims into a standard `ClaimsPrincipal`. Controllers still use `[Authorize]` and `User.Identity` as normal.
- **Best for:** New projects that want access to Descope-specific features (permissions, step-up auth, richer session metadata), or multi-framework teams wanting consistent validation behavior across React/Node/.NET.

#### Side-by-Side Comparison

| Aspect | `JwtBearer` | `DescopeSdk` |
|--------|-------------|--------------|
| Validation library | `Microsoft.AspNetCore.Authentication.JwtBearer` | `Descope` .NET SDK |
| Cookie-to-header middleware | Required (`CookieToAuthHeaderMiddleware`) | Not needed |
| Descope SDK at validation time | Not used | Required |
| Configuration | Authority + Issuer + Audience | Just Project ID |
| OIDC-standard tooling compatible | Yes | N/A |
| Access to Descope-specific features | Via standard JWT claims only | Via SDK Token object (permissions, step-up, etc.) |
| Vendor lock-in at validation layer | None — standard OIDC | Descope SDK dependency |

### Cookie Strategy

| Cookie | Contents | Purpose |
|--------|----------|---------|
| `DS` | Session JWT | Validated on every request |
| `DSR` | Refresh token | Used to silently obtain new session JWTs |

Session and refresh token lifetimes are configurable in the [Descope Console](https://app.descope.com/) under **Project Settings > Session Management > Token Expiration**. Session token timeout must be at least 3 minutes; refresh token timeout controls how long before the user must log in again. These can also be overridden per-tenant.

Both cookies are `HttpOnly` (no JavaScript access), `Secure` in production, and `SameSite=Strict`.

### JWT Templates & Custom Claims

Descope's default session JWT only contains structural claims (`sub`, `iss`, `exp`, `iat`, `aud`, `dct`, `tenants`, `roles`, `permissions`). User profile fields like `name` and `email` are **not included by default** — you need to configure them explicitly via a [JWT Template](https://docs.descope.com/management/jwt-templates) or a [Custom Claims action](https://docs.descope.com/flows/actions/custom-claims) in your flow.

**This demo requires a JWT Template** so that `User.Identity.Name` and email display correctly in the navbar and dashboard.

#### Setting Up the JWT Template

1. In the [Descope Console](https://app.descope.com/settings/project/jwt), go to **Project Settings > JWT Templates**
2. Click **+ JWT Template** and select **Default User JWT**
3. Under **Custom Claims**, add dynamic values:

| Claim Key | Dynamic Value | Description |
|-----------|---------------|-------------|
| `name` | `user.name` | User's display name |
| `email` | `user.email` | User's email address |

4. Go to **Project Settings > Session Management** and set the **User JWT Token Format** to the template you created
5. Click **Save**

Other useful dynamic values you can add include `user.phone`, `user.picture`, `user.givenName`, `user.familyName`, `user.customAttributes.<name>`, and `tenant.name`. See the [Dynamic Keys reference](https://docs.descope.com/flows/dynamic-keys) for the full list.

#### JWT Templates vs Custom Claims in Flows

Descope provides two ways to add claims to JWTs:

| Approach | Scope | Best For |
|----------|-------|----------|
| **[JWT Templates](https://docs.descope.com/management/jwt-templates)** | Project-wide, applied to all JWTs | Consistent claims needed across all flows (name, email, tenant info) |
| **[Custom Claims in Flows](https://docs.descope.com/flows/actions/custom-claims)** | Per-flow, applied during flow execution | Flow-specific data (step-up auth context, conditional claims) |

Custom claims set in a flow override JWT Template values for the same key. Both approaches produce secure claims (not placed in the `nsec` envelope that client-added claims use).

#### Authorization Claims Configuration

JWT Templates also let you control how authorization claims (roles, permissions, tenants) are structured:

- **Default Descope JWT** — Project-level roles in the root, tenant-specific roles nested inside each tenant object
- **Current Tenant, No Tenant Reference** — Tenant-specific roles appear at the root, omitting the `tenants` claim (useful for API gateways that can't parse nested JSON)
- **No Descope Claims** — Excludes all Descope authorization claims, only including your custom claims

#### How This Demo Handles Claim Mapping

In **JwtBearer mode**, ASP.NET Core's JwtBearer handler automatically maps the JWT `name` claim to `ClaimTypes.Name` via standard OIDC claim mapping — no code changes needed once the JWT Template is configured.

In **DescopeSdk mode**, the [`DescopeSdkAuthHandler`](DescopeDemo.Web/Middleware/DescopeSdkAuthHandler.cs) explicitly maps `name` → `ClaimTypes.Name` and `email` → `ClaimTypes.Email` when building the `ClaimsPrincipal`, and configures the `ClaimsIdentity` with the correct name/role claim types.

#### Important Notes

- **Dynamic claims auto-refresh**: When a user attribute used as a custom claim changes, the JWT reflects the new value on the next session refresh — no extra API calls needed.
- **Cookie size limit**: Custom claims increase token size. Browsers enforce a 4KB cookie limit, so avoid storing large payloads. Keep claims minimal ([security best practices](https://docs.descope.com/security-best-practices/custom-claims)).
- **Never store sensitive data in JWTs**: JWTs are Base64-encoded (not encrypted). Don't put secrets, passwords, or sensitive PII in custom claims.
- **Terraform support**: JWT Templates can be managed as code via the [Descope Terraform provider](https://docs.descope.com/managing-environments/terraform).

## Key Features

- **Descope Flows** — `sign-up-or-in-passwords` flow embedded via the `<descope-wc>` Web Component — zero custom login UI code
- **Dual Validation** — Toggle between Microsoft's JwtBearer middleware and Descope's .NET SDK with a single `appsettings.json` change (`Authentication:ValidationMode`)
- **Tenant SSO App Tiles** — Dashboard discovers and displays a tenant's SSO apps from the Descope Management API with IdP-initiated SSO links
- **Silent Token Refresh** — Middleware automatically refreshes expired sessions using the refresh token
- **User Migration** — Bulk import users with bcrypt/PBKDF2/argon2 hashed passwords via the included CLI tool

## Project Structure

The solution follows .NET best practices:

- **`Directory.Build.props`** — Centralizes build settings (`TreatWarningsAsErrors`, `Nullable`, `LangVersion`) across all projects
- **`Directory.Packages.props`** — Central Package Management (CPM) — all NuGet versions in one file
- **`global.json`** — Pins .NET SDK version for consistent builds
- **Strongly-typed configuration** — `DescopeOptions` validated at startup via `ValidateOnStart()` (fail fast on misconfiguration)
- **Sealed classes** — All types sealed by default for JIT devirtualization and clear API intent
- **`IHttpClientFactory`** — All HTTP calls use factory-managed clients (no socket exhaustion)

## Documentation

- [Workshop Guide](docs/workshop-guide.md) — Step-by-step walkthrough
- [Migration Before & After](docs/migration-before-after.md) — What was replaced and why
- [Descope Configuration](docs/descope-configuration.md) — Console setup guide
