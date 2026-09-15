# API Reference

Base URL (dev): `http://localhost:5080`

All responses (JSON endpoints):

```json
{ "success": true, "data": {}, "message": null, "errors": null }
```

Authorize with: `Authorization: Bearer <token>`

## Auth

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/auth/login` | Anonymous | Login |
| GET | `/api/auth/me` | JWT | Current user |
| POST | `/api/auth/logout` | JWT | Client-side logout ack |

## Visitors

| Method | Path | Roles | Description |
|--------|------|------|-------------|
| GET | `/api/visitors` | Authenticated | Search (query filters + paging) |
| POST | `/api/visitors` | Reception/Admin | Register visitor |
| POST | `/api/visitors/expected` | Reception/Host/Admin | Pre-register |
| GET | `/api/visitors/{id}` | Authenticated | Details |
| GET | `/api/visitors/inside` | Authenticated | Currently inside |
| GET | `/api/visitors/expected` | Authenticated | Expected list |
| POST | `/api/visitors/{id}/check-in` | Reception/Security/Admin | Check-in + pass |
| POST | `/api/visitors/{id}/check-out` | Reception/Security/Admin | Check-out |

## Approvals

| Method | Path | Roles |
|--------|------|------|
| GET | `/api/approvals` | Host/Admin |
| POST | `/api/approvals/{id}/approve` | Host/Admin |
| POST | `/api/approvals/{id}/reject` | Host/Admin (body: `{ reason }`) |

## Pass / Verify

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/pass/{visitId}` | Get pass |
| POST | `/api/pass/{visitId}/generate` | Reprint |
| GET | `/api/pass/verify/{visitNumber}` | Lookup by human-readable visit number |

Visitor passes display a Visit Number (e.g. `VMS-2026-000184`). QR codes are not used.

## Masters / Users / Settings / Reports / Audit

- `GET/POST/PUT /api/masters/departments|hosts|purposes|locations|id-types|entry-gates|exit-gates`
- `POST /api/masters/{type}/{id}/deactivate`
- `GET/POST/PUT /api/users`
- `GET /api/settings` (auth) · `GET /api/settings/branding` (public branding only)
- `GET /api/dashboard` (requires `visitor-management`)
- `GET /api/reports/visitors?format=json|excel|pdf|csv` (requires `visitor-management`)
- `GET /api/audit`
- `GET /api/media/{fileName}` (auth; tenant-private storage only)
- `GET /api/system/health|version|license`

## Emergency / Analytics (entitlement-gated)

| Method | Path | Module required |
|--------|------|-----------------|
| GET | `/api/emergency/inside` | `emergency-management` |
| GET | `/api/analytics/summary` | `analytics` |

Disabled or expired modules return **403**. Missing tenant claim on authenticated requests returns **401**.

Tenant context is taken from the JWT only — `X-Tenant-Id` and forged payload tenant fields are ignored.

Swagger UI (Development only): `/swagger`
