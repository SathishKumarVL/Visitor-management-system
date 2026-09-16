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

Whether a visit reaches this queue at all is tenant configuration: `ApprovalRequired` and
`WalkInApprovalRequired` in settings. With both off, registration goes straight to `Approved`
and the queue stays empty. A rejection reason is required. A host may only decide on their own visits.

Check-in is refused with **400** for a visit that is rejected, cancelled, pending approval,
already inside, or already checked out. Check-out is refused with **400** unless the visitor
is currently inside. See `docs/CORE_WORKFLOW_REVIEW.md` for the full state machine.

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
| GET | `/api/emergency/roster` | `emergency-management` |
| POST | `/api/emergency/roll-call` | `emergency-management` |
| GET | `/api/analytics/summary` | `analytics` |

`/api/emergency/roster` returns everyone currently inside plus the latest roll-call mark and the
verified / evacuated / missing / unaccounted counts. `employeeTrackingAvailable` is `false`
because employee presence is not tracked; clients must not present the total as all people on site.

`POST /api/emergency/roll-call` takes `{ visitId, status, notes? }` where `status` is
`1` Verified, `2` Evacuated or `3` Missing. Each call appends an audited event and is refused with
**400** unless the visitor is currently inside. Marks never modify the visit record.

Disabled or expired modules return **403**. Missing tenant claim on authenticated requests returns **401**.

Tenant context is taken from the JWT only — `X-Tenant-Id` and forged payload tenant fields are ignored.

Swagger UI (Development only): `/swagger`
