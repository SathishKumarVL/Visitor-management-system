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

## Pass / QR

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/pass/{visitId}` | Get pass |
| POST | `/api/pass/{visitId}/generate` | Reprint |
| GET | `/api/pass/scan/{passCode}` | Lookup by QR payload |

QR payload contains **only** the pass code (no personal data).

## Masters / Users / Settings / Reports / Audit

- `GET/POST/PUT /api/masters/departments|hosts|purposes|locations|id-types|entry-gates|exit-gates`
- `POST /api/masters/{type}/{id}/deactivate`
- `GET/POST/PUT /api/users`
- `GET/PUT /api/settings`
- `GET /api/dashboard`
- `GET /api/reports/visitors?format=json|excel|pdf|csv`
- `GET /api/audit`

Swagger UI (Development only): `/swagger`
