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
- `TenantId` on users, masters, visitors, visits, settings
- Per-request `ITenantContext` bound from JWT claims (never from client-supplied headers alone)
- EF global query filters assist isolation; authorization still validates host/role scope

## Security highlights

- Secrets via User Secrets / environment / future vault providers
- Fail-closed required crypto configuration
- Authenticated private visitor media (`/api/media`)
- Refresh tokens + short-lived access JWT
- MustChangePassword gate
- Login rate limiting

## Productization foundation

Tables/entities for:

- Product modules
- Tenant entitlements
- Licenses (edition, limits, graceful expiry)
- Feature flags
- Application release history

Backend must enforce entitlements; frontend hiding is UX only.

## API

Current routes remain under `/api/...` (treated as v1).  
Future breaking changes should introduce `/api/v2/...` without removing v1 prematurely.

## Storage

Visitor media:

`App_Data/media/tenants/{tenantId}/visitors/`

Legacy `wwwroot/uploads` is no longer publicly served.
