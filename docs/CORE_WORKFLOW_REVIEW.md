# Core Workflow & Reception Experience Review

**Slice:** Core visitor workflow hardening (focused; builds on the tenant isolation slice)
**Date:** 2026-09-16
**Verdict:** The visitor lifecycle is now enforced server-side and covered by automated tests. This is **not** a claim of production readiness — open blockers are listed at the end.

| Area | Result |
|------|--------|
| Lifecycle state machine enforced server-side | **PASS** |
| Approval configurable per tenant | **PASS** |
| Rejected / cancelled / pending visits blocked from check-in | **PASS** |
| Duplicate check-in, duplicate and invalid check-out blocked | **PASS** |
| Visit number format, uniqueness, concurrency, stability | **PASS** |
| Visitor pass issued only for a visit cleared to enter | **PASS** |
| Report isolation — JSON, CSV, Excel, PDF | **PASS** |
| Notification outbox tenant scoping | **PASS** |
| Login rate limit production safety | **PASS** |
| Emergency roll call persisted and audited | **PASS** |
| Host approval queue UI | **PASS** |
| Employee presence during an emergency | **NOT APPLICABLE** (not tracked; reported as unavailable rather than invented) |
| Site-level authorization | **NOT APPLICABLE** (foundation only, unchanged this slice) |
| Full WCAG 2.2 audit | **PARTIAL** (targeted fixes only; see Accessibility) |
| B-002 SMTP credential rotation | **BLOCKED** (human action) |
| B-003 PII key migration | **BLOCKED** (human action) |
| B-004 MailKit advisory | **BLOCKED** (human action) |

---

## State machine

`VisitStatus` values are `Expected`, `PendingApproval`, `Approved`, `Rejected`, `Cancelled`, `Inside`, `CheckedOut`.

### Approval required (tenant setting `ApprovalRequired` / `WalkInApprovalRequired` = true)

```
REGISTER ──> PENDING_APPROVAL ──approve──> APPROVED ──check-in──> INSIDE ──check-out──> CHECKED_OUT
                    │
                    └──reject──> REJECTED   (terminal)
```

### Approval not required (default)

```
REGISTER ──> APPROVED ──check-in──> INSIDE ──check-out──> CHECKED_OUT
```

### Pre-registration

```
CREATE EXPECTED ──> EXPECTED ──check-in──> INSIDE        (when approval is off)
                        │
                        └──arrival registration──> CANCELLED (superseded, retained as history)
```

An `EXPECTED` visit may only be checked in directly when the tenant has approval turned off. With approval on it must go through a decision first.

### Refused transitions

| From | Action | Result |
|------|--------|--------|
| `Rejected` | check-in | rejected — "This visit was rejected and cannot be checked in." |
| `Cancelled` | check-in | rejected — "This visit was cancelled and cannot be checked in." |
| `PendingApproval` | check-in | rejected — "Visitor is awaiting host approval…" |
| `Expected` + approval required | check-in | rejected — "Visitor must be approved before check-in." |
| `Inside` | check-in | rejected — "Visitor is already checked in." |
| `CheckedOut` | check-in | rejected — "VISITOR ALREADY CHECKED OUT" |
| `CheckedOut` | check-out | rejected — "VISITOR ALREADY CHECKED OUT" |
| anything not `Inside` | check-out | rejected — "Visitor is not currently inside." |
| not `PendingApproval` | approve / reject | rejected — "Visit is not pending approval." |

Guards live in `VisitorService.EnsureCheckInAllowed` and `VisitorService.EnsureCheckOutAllowed`. They are pure functions over the visit and tenant settings, so they are enforced regardless of which client calls the API.

---

## Business rules

| Rule | Where enforced |
|------|----------------|
| No duplicate active check-in | `EnsureCheckInAllowed` |
| No duplicate check-out | `EnsureCheckOutAllowed` |
| Cannot check out a visitor who is not inside | `EnsureCheckOutAllowed` |
| Cannot check in a rejected or cancelled visitor | `EnsureCheckInAllowed` |
| Cannot check in a pending-approval visitor | `EnsureCheckInAllowed` |
| Approval requirement is tenant configuration | `SettingsService` keys `ApprovalRequired`, `WalkInApprovalRequired` |
| Pass only for a visit cleared to enter | `RegisterAsync` issues a pass only at `Approved`; otherwise issued at check-in |
| Visit number unique and human readable | unique index `(TenantId, VisitNumber)` + allocation under a per-tenant lock |
| Pre-registration retained, not deleted | arrival registration sets the expected visit to `Cancelled` with a note |
| Emergency state never rewrites a visit | marks are rows in `EmergencyRollCallEvents` |

Nothing here relies on frontend validation. The wizard's own checks are a convenience layer only.

---

## Visit number

Format: `PREFIX-YYYY-NNNNNN`, e.g. `TIA-2026-000184`. The prefix comes from the tenant's `VisitorIdPrefix` setting and defaults to `VMS` when unset.

- **Uniqueness** — unique index on `(TenantId, VisitNumber)`.
- **Concurrency** — allocation and insert happen inside one transaction that holds a SQL Server application lock keyed on tenant and year, so two simultaneous registrations cannot read the same highest number. A retry on unique-index violation remains as a backstop for numbers created outside this path. Providers without application locks fall back to the index plus retry.
- **Tenant behaviour** — each tenant keeps an independent sequence because the lookup runs through the tenant query filter.
- **Historical stability** — the number is assigned once at creation and never regenerated; check-in and check-out do not touch it.
- **Searchability** — matched by visitor search, the currently-inside search, the approvals queue and pass verification.
- **No database ids are exposed** to visitors; the pass and all printed output carry the visit number only.

Visitor numbers (`PREFIX-V-YYYYMMDD-NNNN`) are allocated by the same locked path.

---

## Approval behaviour

Configuration is per tenant, with walk-ins and scheduled visits gated independently. With approval enabled, registration parks the visit at `PendingApproval` and creates a pending `Approval` row addressed to the host.

Approve and reject are restricted to `SuperAdmin`, `Admin` and `Host` at the controller, and `EnsureHostAuthorization` further restricts a host to their own visits. Rejection requires a non-empty reason. Every decision records the actor (`ActionByUserId`), the timestamp (`ActionAt`), the visit, the tenant and, for rejections, the reason — plus an audit log entry.

Reception no longer fires a blind check-in after registration. When the visit comes back pending, the wizard returns to reception with an explicit waiting-for-host message.

---

## Check-in / check-out behaviour

Check-in resolves an entry gate (explicit, then tenant default, then any active gate), sets `CheckInAt`, records the acting user, and issues or reuses an active pass. Check-out resolves an exit gate the same way, sets `CheckOutAt`, deactivates passes, and writes an audit entry.

The thank-you email is queued on a background task so SMTP can never delay or fail the checkout transaction. That background scope is explicitly seeded with the visit's tenant, because it has no request to resolve one from.

---

## Pass behaviour

The pass carries tenant branding, visitor photo, name, company, host, department, purpose, location, visit number, entry time and status. It contains **no QR code and no barcode**, and none is to be reintroduced. Branding comes from tenant settings rather than hardcoded values.

A pass is only created for a visit that is cleared to enter: at registration when approval is off, otherwise at check-in. A pending-approval visit has no pass.

---

## Notification behaviour

`NotificationOutbox` rows now carry the `TenantId` that queued them, sit behind a fail-closed query filter, and are refused entirely if there is no tenant context. This is the code-only hardening needed so a future delivery worker cannot process one tenant's notification using another tenant's configuration. No existing rows were deleted; the column is nullable so historical rows survive, and the seeder backfills them to the default tenant.

Notification failure never fails a checkout.

---

## Emergency behaviour

The roster lists everyone currently inside with photo, name, company, host, location, entry time and status, plus counts for verified, evacuated, missing and not-yet-marked.

Marks are appended to `EmergencyRollCallEvents` — tenant scoped, attributed to the acting user, timestamped and audited. Corrections append a new event rather than overwriting, so the roll call remains auditable. **No emergency action modifies the visit record.**

Employee presence is not tracked by this system. The roster reports `employeeTrackingAvailable: false` and the UI says so explicitly rather than presenting a total that silently excludes staff.

---

## Authorization summary

| Capability | Roles |
|------------|-------|
| Register / check-in / check-out | SuperAdmin, Admin, Reception, Security |
| Approve / reject | SuperAdmin, Admin, Host (host limited to own visits) |
| Emergency roster and roll call | SuperAdmin, Admin, Reception, Security |
| Reports | per existing report controller policy |
| Settings update | SuperAdmin, Admin |

All visitor endpoints remain behind the `VisitorManagement` module entitlement; emergency endpoints behind `EmergencyManagement`.

---

## Rate limiting

The login limiter decision now lives in `RateLimitPolicy.IsLoginRateLimitRelaxed`. Only the exact, case-sensitive environment names `Development` and `Testing` are relaxed. `Production`, `Staging`, lower-case variants, padded values, comma-joined values and an unset environment all stay rate limited. This is asserted by unit tests, including one that fails if `Program.cs` reverts to an inline environment check.

---

## Accessibility

Targeted improvements in this slice: polite live regions for approval, check-out and emergency results; a labelled rejection reason field with `aria-invalid`, `aria-describedby`, Enter to submit and Escape to cancel; focus moved into the reason field when a rejection starts; `aria-pressed` on roll-call toggles; a labelled roll-call button group; `htmlFor`/`id` pairing fixed on the exit-gate select.

Already in place from earlier work: 44px+ touch targets across buttons and form controls, real `<label>` elements, `role="alert"` on field errors, `role="status"` on alerts and spinners.

**PARTIAL** — not yet done: there is no modal/dialog component in the app, so `role="dialog"`, `aria-modal` and focus trapping are untested because there is nothing to apply them to; a full contrast audit and a keyboard-only pass over every screen have not been performed.

---

## Test coverage

78 backend tests pass. Added this slice:

| Area | Tests |
|------|-------|
| Registration, approval disabled | status is `Approved`, check-in succeeds |
| Registration, approval enabled | status is `PendingApproval`, check-in refused |
| Approval | approve makes the visit check-in-able |
| Rejection | rejected visit cannot check in; reason required; actor and timestamp recorded |
| Authorization | Security cannot approve; Host cannot open the emergency roster |
| Check-in | duplicate refused; checked-out visitor cannot re-enter; cancelled visit refused |
| Check-out | succeeds, duplicate refused, refused when not inside |
| Visit number | format, uniqueness under 6 concurrent registrations, stability across the lifecycle |
| Expected visitor | pre-registration is cancelled, not deleted, on arrival |
| Pass | issued at check-in with no QR payload; not issued while pending approval |
| Audit | check-in and check-out both logged |
| Emergency | roster counts, mark persisted + audited + visit untouched, corrections append, mark refused when not inside |
| Report isolation | JSON, CSV, Excel and PDF, each with a positive control; totals/counts; filenames |
| Rate limit config | relaxed environments, everything else limited, bounded production limit, no inline check in `Program.cs` |

Each report isolation test first asserts the extractor **can** find Tenant B's markers in Tenant B's own export. Without that positive control, an extractor that silently returned nothing would look like a pass. PDF extraction uses PdfPig (test-only dependency) because the generator subsets its fonts, so the exported text is not readable as raw bytes.

Test parallelism is disabled at assembly level: all test classes share one database and some flip tenant settings.

---

## Known limitations

1. **Employee emergency tracking** — not supported by the data model; deliberately not invented.
2. **Site-level authorization** — still foundation only, unchanged by this slice.
3. **No modal component** — dialog semantics and focus trapping remain untested for that reason.
4. **Application-lock dependency** — visit number serialisation uses a SQL Server application lock. On another provider the unique index plus retry is the only protection, which is correct but can surface a failure under heavy contention.
5. **Outbox delivery worker** — the tenant column is in place, but no background delivery worker exists yet; whoever writes one must resolve configuration from the row's `TenantId`.
6. **Frontend test coverage** — the frontend has a production build gate only; there are no component tests for the new approvals or emergency screens.
7. **Accessibility** — targeted fixes only, no full WCAG 2.2 audit.

## Blockers (human action, unchanged)

- **B-002** — SMTP credential rotation.
- **B-003** — PII encryption key migration.
- **B-004** — MailKit moderate severity advisory.
