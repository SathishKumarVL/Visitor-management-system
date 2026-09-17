# Tenant Isolation & Entitlement Review

**Slice:** Tenant isolation & entitlement hardening (focused; not overnight roadmap)  
**Date:** 2026-09-15  
**Verdict:** Foundation hardened with automated negative tests — **not** claimed SaaS-complete.

| Area | Result |
|------|--------|
| Tenant isolation (core resources) | **PASS** |
| Negative Tenant A/B tests | **PASS** |
| IDOR / forged tenant header | **PASS** |
| Media storage isolation | **PASS** |
| Report isolation (JSON/CSV exercised) | **PASS** |
| Module entitlement backend enforcement | **PASS** |
| License expiry fail-closed | **PASS** |
| Site-level authorization | **NOT APPLICABLE** (foundation only; gap documented) |
| Full Excel/PDF report matrix automation | **PARTIAL** (CSV/JSON covered; Excel/PDF use same QueryRows) |
| B-002 SMTP rotation | **BLOCKED** |
| B-003 PII key migration | **BLOCKED** |
| B-004 MailKit advisory | **BLOCKED** |

---

## Resources reviewed

| Resource | Tenant scoped? | Mechanism |
|----------|----------------|-----------|
| Users | Yes | `TenantId` + query filter |
| Employees / Departments | Yes | `TenantId` + filter |
| Locations / Purposes | Yes | `TenantId` + filter |
| IdTypes | Yes (hardened this slice) | `TenantId` + filter + migration backfill |
| Visitors / Visits | Yes | `TenantId` + filter |
| Photos / Documents | Indirect | Parent visitor filtered; media path tenant-rooted |
| Passes / Approvals | Indirect | Parent visit filtered |
| Settings | Yes | `TenantId` + filter + **per-tenant cache** |
| Audit logs | Yes | Nullable `TenantId` + filter |
| Reports | Yes | Via filtered `VisitorVisits` |
| Emergency / Currently inside | Yes | Filtered visits + module gate |
| Product entitlements / licenses | Explicit `TenantId` queries | `IEntitlementService` |

---

## Authorization model

1. Authenticate (JWT).
2. `TenantResolutionMiddleware` binds `ITenantContext` from JWT `tenantId` only.
3. Missing/invalid/inactive tenant → **401** (fail closed).
4. EF global filters: unset tenant context → **empty result set** (never all tenants).
5. Role/permission checks remain on controllers.
6. Host scope still applies for Host role approvals/search.
7. Module entitlements via `[RequireModule]` → **403** when disabled/expired/license invalid.
8. Client `X-Tenant-Id` / payload TenantId is **not** trusted.

Site claim (`siteId`) is stored when valid for the tenant; **full site-scoped data authorization is not implemented** (documented gap — do not invent a large site auth system here).

---

## Endpoints reviewed (summary)

| Group | WHO | TENANT | ROLE | MODULE |
|-------|-----|--------|------|--------|
| `/api/visitors*` | Authenticated | JWT | Role per action | `visitor-management` |
| `/api/approvals*` | Host/Admin | JWT | Host/Admin | `visitor-management` |
| `/api/pass*` | Auth / Security roles | JWT | Role per action | `visitor-management` |
| `/api/reports/*` | Admin/Reception | JWT | Role | `visitor-management` |
| `/api/dashboard` | Auth | JWT | Auth | `visitor-management` |
| `/api/emergency/inside` | Ops roles | JWT | Role | `emergency-management` |
| `/api/analytics/summary` | Admin | JWT | Admin | `analytics` |
| `/api/media/{file}` | Auth | JWT private root only | Auth | (tenant path) |
| `/api/settings` | Auth | JWT | Auth / Admin write | — |
| `/api/settings/branding` | Anonymous | Explicit TIAANO only | — | — |
| `/api/masters*` | Auth | JWT | Admin write | — |
| `/api/users*` | Admin | JWT | Admin | MaxUsers on create |

Gaps closed this slice: shared IdTypes, settings static cache leak, media legacy shared-root reads, soft tenant fallback on authenticated requests, missing module enforcement.

---

## Tests created

File: `backend.Tests/TenantIsolationTests.cs` (plus existing workflow/security suite).

### Negative tenant isolation

- Tenant A search cannot list Tenant B visitors
- Tenant A GET Tenant B visit → 404
- Tenant A settings ≠ Tenant B company
- Tenant A media cannot open Tenant B file (+ traversal attempt)
- Tenant A reports/CSV exclude Tenant B records
- Tenant A pass for Tenant B visit → 404
- Tenant A masters exclude Tenant B departments
- Forged `X-Tenant-Id` does not switch tenant
- Tenant B can still read own visitor (**positive control**)

### Entitlement

- Analytics disabled (default seed) → 403
- Emergency disabled for Tenant B → 403; visitor API still OK
- Visitor-management disabled → 403 on visitor API
- Expired license (grace 0) → 403 on module endpoints

**Quality gate:** 35 backend tests passed; frontend production build succeeded.

---

## Storage isolation

- Write/read path: `App_Data/media/tenants/{tenantId:N}/visitors/`
- Legacy shared roots **no longer opened** by `OpenAsync` (fail closed)
- Path traversal / nested names rejected
- Not publicly mapped via static files

Result: **PASS**

---

## Report isolation

Reports use EF-filtered `VisitorVisits` (`QueryRows`). JSON + CSV negative tests confirm Tenant B rows absent. Excel/PDF share the same query path (not separately automated).

Result: **PASS** (shared query path); Excel/PDF dedicated assertions **PARTIAL**

---

## Fail-safe behavior

| Condition | Behavior |
|-----------|----------|
| Missing tenant claim (authenticated) | 401 |
| Inactive tenant | 401 |
| Entitlement missing / disabled | 403 |
| License expired past grace | 403 |
| License revoked | 403 |
| Forged tenant header | Ignored |
| Unset tenant + EF query | Empty (fail closed) |
| Public branding | Explicit WellKnown TIAANO only |

---

## Known limitations

1. ~~Site-scoped authorization not enforced (SiteId claim advisory).~~ **Enforced** — see `docs/SITE_SCOPING.md`. User administration remains tenant-wide by design.
2. Approvals/Passes/Photos lack direct `TenantId` columns (rely on parent filters) — sound for current model; document for future hardening.
3. Notification outbox not tenant-columned (SMTP still B-002).
4. Background jobs: no multi-tenant job runner yet.
5. Not SaaS-ready: no tenant provisioning portal, billing, or cross-region isolation claims.
6. Login rate limit disabled in Development (enabled outside Development).

---

## Remaining blockers

Unchanged: **B-002**, **B-003**, **B-004** (see `docs/OVERNIGHT_BLOCKERS.md`).

---

## Loader asset

Canonical asset: `frontend/public/branding/tiaano-loader.svg` (integrated in UI).  
Duplicate root `tiaano loader.svg` removed as accidental duplicate.
