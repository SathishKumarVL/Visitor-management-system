# Architecture — TIAANO Visitor Management Platform

Current application version: **0.2.0-overnight**

## Shape

Modular monolith:

- React + TypeScript + Vite frontend (PWA-ready direction)
- ASP.NET Core 9 Web API
- SQL Server + EF Core
- On-premise first (Windows Server / IIS), cloud-ready later

## Layers

1. **Core platform** — auth, tenancy, licensing, media, settings, upgrades
2. **Visitor Management module** — registration, approval, check-in/out, passes, reports, emergency roster

The visitor lifecycle is a server-enforced state machine. `VisitorService.EnsureCheckInAllowed`
and `EnsureCheckOutAllowed` are the single gate for every entry and exit transition, and whether
a visit needs approval is read from tenant settings rather than compiled in. Emergency roll call
is modelled as an append-only event stream (`EmergencyRollCallEvents`) so operational marshalling
never rewrites visit history. See `docs/CORE_WORKFLOW_REVIEW.md` for the full state map.

TIAANO is the first tenant (`WellKnownTenants.TiaanoId`), not hardcoded business logic in reusable modules.

## Multi-tenancy

- `Tenant` / `Site` entities
- `TenantId` on users, masters (including IdTypes), visitors, visits, settings
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
| `emergency-management` | `/api/emergency/inside`, `/api/emergency/roster`, `/api/emergency/roll-call` |
| `analytics` | `GET /api/analytics/summary` |

Also enforced: license expiry (past grace), revoked license, MaxUsers on user create.

Frontend feature hiding is UX only — not security.

## API

Current routes remain under `/api/...` (treated as v1).  
Future breaking changes should introduce `/api/v2/...` without removing v1 prematurely.

## Face recognition

Recognition runs entirely on the server (`backend/Services/Face/`). The browser only uploads the
captured image; it never computes or submits a face template. That matters because a template the
client supplies is a credential — anyone able to post one could claim an arbitrary visitor's identity.

| Stage | Implementation |
| --- | --- |
| Detection | SCRFD (`det_10g.onnx`), 640×640 letterboxed input, three FPN levels |
| Alignment | Umeyama similarity transform onto the canonical ArcFace landmarks, warped to 112×112 |
| Embedding | ArcFace (`w600k_r50.onnx`), 512 dimensions, L2-normalised |
| Matching | Cosine similarity, threshold `FaceRecognition:MatchThreshold` (default 0.42) |

Both models run through ONNX Runtime on CPU. `InsightFaceService` is a singleton because the sessions
hold roughly 180 MB of weights and are thread-safe for concurrent inference; loading is deferred to
first use, so a deployment without the weights degrades to "unavailable" instead of failing to start.

Templates live in `VisitorFaceDescriptors`, tagged with the model that produced them so generations
are never cross-compared. Up to `FaceRecognition.MaxTemplatesPerVisitor` recent captures are kept per
visitor — matching against several poses is markedly more reliable than against the newest alone.
Templates from the earlier browser-side face-api.js implementation remain in the table under
`faceapi-128` and are ignored; those visitors re-enrol on their next registration.

Two entry points use it: `POST /api/visitors/face-search` recognises a returning visitor during
registration, and `POST /api/visitors/face-identify-inside` recognises someone currently inside for
face-driven check-out. The operator confirms every registration match before it is applied.

## Storage

Visitor media:

`App_Data/media/tenants/{tenantId}/visitors/`

Legacy `wwwroot/uploads` is no longer publicly served.
