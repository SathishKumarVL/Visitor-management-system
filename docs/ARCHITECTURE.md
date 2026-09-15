# Architecture — TIAANO Visitor Management Platform

Current application version: **0.2.0-overnight**

## Shape

Modular monolith:

- React + TypeScript + Vite frontend (PWA-ready direction)
- ASP.NET Core 9 Web API
- SQL Server + EF Core
- On-premise first (Windows Server / IIS), cloud-ready later

## Layers

1. **Core platform** — auth, tenancy, licensing, media, audit, settings, upgrades
2. **Visitor Management module** — registration, check-in/out, passes, reports, emergency roster

TIAANO is the first tenant (`WellKnownTenants.TiaanoId`), not hardcoded business logic in reusable modules.

## Multi-tenancy

- `Tenant` / `Site` entities
- `TenantId` on users, masters (including IdTypes/EntryGates/ExitGates), visitors, visits, settings
- Per-request `ITenantContext` bound from JWT `tenantId` claim only (client headers ignored)
- Authenticated requests without a valid active tenant claim fail closed (401)
- EF global query filters fail closed: missing tenant context returns **no rows** (never all tenants)
- Site claim is advisory foundation only — full site-scoped authorization is not yet enforced

**This is a multi-tenant foundation, not a claim of SaaS-ready isolation for every edge case.**

## Security highlights

- Secrets via User Secrets / environment / future vault providers
- Fail-closed required crypto configuration
- Authenticated private visitor media (`/api/media`) under `App_Data/media/tenants/{tenantId}/visitors/` only
- Refresh tokens + short-lived access JWT
- MustChangePassword gate
- Login rate limiting (disabled in Development for local testing)

## Productization / entitlements

Backend-authoritative module checks via `[RequireModule]`:

| Module key | Enforced on |
|------------|-------------|
| `visitor-management` | Visitors, approvals, passes, dashboard, reports |
| `emergency-management` | `GET /api/emergency/inside` |
| `analytics` | `GET /api/analytics/summary` |

Also enforced: license expiry (past grace), revoked license, MaxUsers on user create.

Frontend feature hiding is UX only — not security.

## API

Current routes remain under `/api/...` (treated as v1).  
Future breaking changes should introduce `/api/v2/...` without removing v1 prematurely.

## Storage

Visitor media:

`App_Data/media/tenants/{tenantId}/visitors/`

Legacy `wwwroot/uploads` is no longer publicly served.
