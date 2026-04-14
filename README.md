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
| [`Program.cs`](DescopeDemo.Web/Program.cs) | Registers the Descope SDK, configures JWT or SDK auth, wires up middleware |
| [`Views/Auth/Login.cshtml`](DescopeDemo.Web/Views/Auth/Login.cshtml) | Embeds the `<descope-wc>` Web Component — replaces all custom login UI |
| [`Controllers/AuthController.cs`](DescopeDemo.Web/Controllers/AuthController.cs) | Handles login page, token callback (stores cookies), and logout |
| [`Middleware/CookieToAuthHeaderMiddleware.cs`](DescopeDemo.Web/Middleware/CookieToAuthHeaderMiddleware.cs) | Bridges browser cookies to Bearer tokens for JwtBearer validation; handles silent token refresh |
| [`Middleware/DescopeSdkAuthHandler.cs`](DescopeDemo.Web/Middleware/DescopeSdkAuthHandler.cs) | Alternative auth handler using the Descope SDK directly instead of JwtBearer |
| [`Services/DescopeSessionService.cs`](DescopeDemo.Web/Services/DescopeSessionService.cs) | Wraps the Descope SDK for session validation, refresh, and logout |
| [`Services/DescopeAppService.cs`](DescopeDemo.Web/Services/DescopeAppService.cs) | Fetches tenant SSO apps from the Descope Management API |
| [`appsettings.json`](DescopeDemo.Web/appsettings.json) | Two settings: `Descope:ProjectId` and `Authentication:ValidationMode` |

### Two Validation Modes

Set `Authentication:ValidationMode` in `appsettings.json`:

- **`JwtBearer`** (default) — Uses ASP.NET Core's built-in JWT middleware. Descope issues standard JWTs, so no Descope-specific code is needed at validation time. Best for most apps.
- **`DescopeSdk`** — Uses the Descope SDK's `ValidateSessionAsync()` via a custom `AuthenticationHandler`. Useful for accessing Descope-specific token features like permissions or step-up auth.

### Cookie Strategy

| Cookie | Contents | Lifetime | Purpose |
|--------|----------|----------|---------|
| `DS` | Session JWT | 1 hour | Validated on every request |
| `DSR` | Refresh token | 30 days | Used to silently obtain new session JWTs |

Both cookies are `HttpOnly` (no JavaScript access), `Secure` in production, and `SameSite=Strict`.

## Key Features

- **Descope Flows** — `sign-up-or-in-passwords` flow embedded via the `<descope-wc>` Web Component — zero custom login UI code
- **Dual Validation** — Toggle between JwtBearer middleware and Descope SDK with a single config change
- **Tenant SSO App Tiles** — Dashboard discovers and displays a tenant's SSO apps from the Descope Management API with IdP-initiated SSO links
- **Silent Token Refresh** — Middleware automatically refreshes expired sessions using the refresh token
- **User Migration** — Bulk import users with bcrypt/PBKDF2/argon2 hashed passwords via the included CLI tool

## Documentation

- [Workshop Guide](docs/workshop-guide.md) — Step-by-step walkthrough
- [Migration Before & After](docs/migration-before-after.md) — What was replaced and why
- [Descope Configuration](docs/descope-configuration.md) — Console setup guide
