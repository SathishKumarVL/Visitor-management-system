# Enterprise Review Gap Audit

**Product:** TIAANO Visitor Management System  
**Mode:** Audit only — no application code, schema, dependency, or config changes  
**Date:** 2026-09-18  
**Inputs:** Repository inspection + `OVERNIGHT_*`, `TENANT_ISOLATION_REVIEW`, `SECURITY`, `ARCHITECTURE`, `DATABASE`, `API`, `DEPLOYMENT_*`, `UPGRADES`, `PRIVACY`, `DATA_ACCESS_REVIEW`, `PASS_NUMBER_REVIEW`  
**Companion:** [`DATA_ACCESS_AUDIT.md`](./DATA_ACCESS_AUDIT.md)

Status vocabulary: **PASS** | **GAP** | **PARTIAL** | **NOT APPLICABLE** | **BLOCKED**

---

## 1. Executive Summary

TIAANO VMS is a **modular monolith** with a credible **multi-tenant foundation**, hardened auth/media paths, site scoping, entitlement gates, and a recent pass-number / ADO.NET / i18n / reminder sprint. It is **not** SaaS-complete and should **not** be marketed as fully production-hardened without closing P1 items.

The review comment “Change raw scripting to ADO” is **partially addressed**: justified ADO.NET exists for atomic pass allocation and applock; EF Core remains primary. Remaining data-access work is **standardization and hygiene**, not a wholesale ORM rewrite.

**Highest residual risks** are operational and reliability: **MailKit advisory (BLOCKED upstream)**, **no CI**, **backup docs pointing at wrong media path**, **timezone inconsistency** (`DateTime.Now` vs `UtcNow`), **JWT in web storage**, **incomplete localization**, and **no durable outbox retry**.

**Update (2026-09-18):** Visit check-in/out optimistic concurrency (**E-005**) is **Fixed** — see [`VISIT_CONCURRENCY_REVIEW.md`](./VISIT_CONCURRENCY_REVIEW.md) (`VisitorVisit.RowVersion`, HTTP 409, tests).

---

## 2. Current Architecture

| Layer | Reality |
|-------|---------|
| Shape | ASP.NET Core 9 API + React/Vite SPA |
| ORM | EF Core SQL Server (primary) |
| Direct SQL | Thin ADO.NET (`Microsoft.Data.SqlClient`) for pass counters + applock |
| Tenancy | JWT `tenantId` + EF global filters + site filters |
| Modules | Entitlements / licenses scaffold; visitor-management primary |
| Workers | `AppointmentReminderWorker` hosted service |
| Deploy target | Windows Server + IIS + SQL Server (documented) |

**Sanity:** **Appropriate** for an on-prem commercial VMS. Not over-engineered into microservices; licensing/upgrade scaffolding is early but present.

---

## 3. Database / Data Access

See **`docs/DATA_ACCESS_AUDIT.md`** for the full occurrence table.

| Status | Summary |
|--------|---------|
| PASS | No request-path SQL injection found; pass allocation parameterized + tenant-scoped |
| PASS | EF for normal CRUD |
| PARTIAL | Written ADO/EF hybrid practice exists in review docs; not CI-enforced |
| GAP | `AdoSql` lacks `CommandTimeout`; seeder still runs static DDL via `ExecuteSqlRaw` |
| GAP | `PassCode` uniqueness is **global** (by design for codes) — operators must never reuse free-text badges |

---

## 4. Security

| Area | Status | Evidence / note |
|------|--------|-----------------|
| Secrets fail-closed | PASS | `SecretConfiguration` rejects short/placeholder keys in Production |
| Security headers | PASS | CSP, XFO DENY, nosniff, Referrer-Policy, Permissions-Policy; HSTS in Production (`SecurityMiddleware`) |
| CORS | PARTIAL | Dev open; Production origin allow-list — verify deploy config always set |
| Media | PASS | Private `/api/media`; branding public under `/branding` |
| SQL injection | PASS | See data-access audit |
| XSS sinks | PASS | No `dangerouslySetInnerHTML` found |
| JWT storage | GAP | Access + refresh tokens in `localStorage`/`sessionStorage` (`frontend/src/lib/api.ts`) — XSS-stealable |
| Audit trail | GAP | `AuditLogs` dropped; commercial “who did what” is thin (approvals/emergency events only) |
| MailKit | BLOCKED | NU1902 GHSA-9j88-vvj5-vhgr on 4.15.1 (`OVERNIGHT_BLOCKERS` B-004) |

---

## 5. Authentication

| Topic | Status | Notes |
|-------|--------|-------|
| JWT access ~30m | PASS | Config-driven |
| Refresh rotation | PASS | Remember-me 14d / else 7d |
| Password policy / lockout | PASS | Identity options |
| MustChangePassword | PASS | Claim + UI gate |
| Login rate limit | PASS | 10/min/IP; relaxed in Dev/Testing |
| Logout revocation | PASS | Refresh tokens invalidated |
| Password reset product UX | PARTIAL | CLI `reset-password` exists; no end-user self-service flow documented as product feature |

---

## 6. Authorization

| Topic | Status | Notes |
|-------|--------|-------|
| Role attributes on controllers | PASS | SuperAdmin/Admin/Reception/Security/Host |
| Host scoping | PASS | Search/detail restricted |
| Site scoping | PASS | Enforced (see `SITE_SCOPING.md`); some older docs still say “advisory” |
| Entitlements | PASS | Module gates fail-closed |
| License expiry | PARTIAL | Hard 403 after grace — not read-only mode claimed in `UPGRADES.md` |
| Menu allowlist | PARTIAL | UX narrowing; API still role-based (by design for v1) |

---

## 7. Multi-Tenancy

| Topic | Status | Notes |
|-------|--------|-------|
| JWT tenant claim | PASS | Header forgery ignored |
| EF filters | PASS | Broad coverage |
| Settings cache | PASS | Static dictionary keyed by `TenantId` |
| Public branding | PARTIAL | Hard-scoped to WellKnown TIAANO tenant — multi-tenant login branding incomplete |
| Reminder offsets | GAP | Worker reads **first** `ReminderHoursBefore` across tenants — not per-tenant |
| Background tenant context | PARTIAL | Reminder worker sets tenant per visit; checkout email uses `Task.Run` + new scope |
| SaaS-ready | NOT APPLICABLE | Explicitly foundation-only |

---

## 8. API

| Topic | Status | Notes |
|-------|--------|-------|
| Consistent `ApiResponse<T>` | PASS | Widely used |
| Exception → 400/403/500 mapping | PARTIAL | `InvalidOperationException` → 400; business conflicts often not 409 |
| URL versioning `/api/v1` | GAP | Unversioned `/api/...`; docs say “v1 compatible” only |
| Pagination | PARTIAL | Some lists paged; reports capped at 5000 |
| Correlation IDs | GAP | Not observed on responses |
| Swagger | PARTIAL | Dev-oriented; ensure disabled/locked in Production IIS |
| Docs completeness | GAP | `API.md` missing refresh, sites, face endpoints; stale audit/logout wording |

---

## 9. Backend

| Topic | Status | Notes |
|-------|--------|-------|
| Fat `VisitorService` (~1420 lines) | GAP | Registration, approval, gates, dashboard, face, numbering, media, email fan-out |
| Fat controllers | PARTIAL | `ApiControllers.cs` aggregates many resources |
| Global exception middleware | PASS | No stack to clients in Production |
| ProblemDetails | INFORMATIONAL | Custom envelope instead of RFC7807 |

---

## 10. Frontend

| Topic | Status | Notes |
|-------|--------|-------|
| TypeScript `any` | PASS | ≈0 |
| Token handling | GAP | Web storage (see Security) |
| Checkout UX | PASS | Inline checkout removed; dedicated `/checkout/face` |
| Verify `?mode=checkout` | PARTIAL | Alternate checkout path still exists — confirm product intent |
| Dead pages | INFORMATIONAL | e.g. unrouted `QrScanPage` / unused pages may remain in tree |
| Error UX | PARTIAL | Local Alert pattern; no React ErrorBoundary |

---

## 11. Database

| Topic | Status | Notes |
|-------|--------|-------|
| Migrations present | PASS | Including DropAuditLogs, PassNumberSeries |
| Soft delete | PARTIAL | `IsActive` deactivation; no standardized soft-delete policy for visits |
| Visit concurrency token | **Fixed** | `VisitorVisit.RowVersion` (`rowversion`) + `SaveVisitLifecycleAsync` → HTTP 409; see `VISIT_CONCURRENCY_REVIEW.md` |
| Indexes | PARTIAL | Strong on visit numbers; report/search growth may need review |
| Seed vs docs | GAP | `DATABASE.md` still describes rich mock masters; seeder is slim |

---

## 12. Performance

| Topic | Status | Notes |
|-------|--------|-------|
| Report `Take(5000)` + wide Includes | GAP | Memory risk at scale |
| Split queries | PASS | Enabled |
| Load/perf suite | GAP | Not present |
| Face ONNX | INFORMATIONAL | CPU-bound; capacity not documented |

---

## 13. Transactions

| Operation | Boundary | Gap |
|-----------|----------|-----|
| Pass allocate | ADO transaction | Solid |
| Visit/visitor numbers | Applock + EF | Solid |
| Registration | Multi-step SaveChanges | Photo/face/pass after visit — partial failure possible |
| Check-in / check-out | Single SaveChanges after guards + rowversion | **Fixed** — concurrent stale writers → 409 |
| Reminder enqueue | Per-message | Idempotency key helps |
| Checkout email | After commit via `Task.Run` | Intentional eventual |

---

## 14. Concurrency

| Scenario | Current | Risk |
|----------|---------|------|
| Dual check-in | Status guard + `RowVersion` | **Fixed** — loser gets 409 or sequential 400 |
| Dual check-out | Same | **Fixed** |
| Pass generation | ADO conditional UPDATE | **PASS** |
| Approvals | Pending approval pick | **PARTIAL** — last-writer / double approve edge cases need explicit tests |
| Reminder jobs | Idempotency keys | **PASS** for duplicate send intent |
| Dual registration same badge | N/A — server allocates | **PASS** vs old `"1"` bug |

---

## 15. Idempotency

| Operation | Behavior | Duplicate risk |
|-----------|----------|----------------|
| Check-in | Rejects if already Inside; concurrent → 409 | **Fixed** race window |
| Check-out | Rejects if already out; concurrent → 409 | **Fixed** race window |
| Pass allocate | Atomic increment | Low |
| Pass reprint | Reuses / creates under ResolvePassCode | Medium if logic drifts |
| Appointment reminder | IdempotencyKey unique per tenant | Low |
| Email outbox | No automatic retry | Failed mail stays unsent (gap: reliability, not dup) |
| Feedback | N/A | Feature absent |

---

## 16. Background Jobs

| Topic | Status | Notes |
|-------|--------|-------|
| Hosted reminder worker | PARTIAL | Exists; uses `DateTime.Now`; cross-tenant scan; first-tenant reminder setting |
| Outbox poller / retry | GAP | Failed `IsSent=false` not reprocessed by worker |
| Shutdown / poison messages | GAP | Not documented/tested |
| Checkout `Task.Run` | GAP | Not durable across process kill |

---

## 17. Notifications

| Topic | Status | Notes |
|-------|--------|-------|
| Outbox abstraction | PASS | Channel + providers (Email, Push stub) |
| Push | PARTIAL | Foundation/stub — not production push |
| SMTP failure | PARTIAL | Error stored; no retry worker |
| Templates | GAP | Ad-hoc strings |
| Preferences | GAP | None |

---

## 18. Appointments

| Topic | Status | Notes |
|-------|--------|-------|
| Model | PASS | `VisitorVisit` Expected / PreRegistered |
| Create API | PASS | |
| Reminders 24h/1h | PARTIAL | Worker + setting; timezone/local clock risk |
| Edit/reschedule product | PARTIAL | Limited compared to full appointment product |
| Approve/reject expected | Via visit workflow | OK |

---

## 19. Feedback

| Status | Notes |
|--------|-------|
| NOT APPLICABLE | No feedback module in code or docs |

---

## 20. Localization

| Topic | Status | Notes |
|-------|--------|-------|
| en/ta resources + switcher | PARTIAL | ~100 keys; parity script |
| Coverage | GAP | ~3/25 pages use `t()`; most UI still English |
| Tamil UX | PARTIAL | Fonts loaded; layout overflow not systematically tested |
| Date/time locale | GAP | Mostly default JS/`DateTime` formatting |

---

## 21. Accessibility

| Topic | Status | Notes |
|-------|--------|-------|
| Some labeled forms | PARTIAL | Login/wizard better |
| FieldLabel without htmlFor | GAP | Hosts/Users/Expected/Reports common pattern |
| Dialogs / focus trap | GAP | Limited dialog usage; prior review PARTIAL |
| Live regions | PARTIAL | Some `aria-live` on inside list |

---

## 22. PWA

| Status | Notes |
|--------|-------|
| PARTIAL / mostly GAP | Online banner only; no service worker/manifest installability |

---

## 23. IIS / Windows

| Topic | Status | Notes |
|-------|--------|-------|
| Deploy docs | PARTIAL | Present |
| Media backup path | GAP | Docs still cite `wwwroot\uploads`; runtime is `App_Data/media` |
| HTTPS/HSTS | PARTIAL | Code supports; IIS binding is operator duty |
| Packaged release artifact | GAP | Not automated |

---

## 24. Backup / Restore

| Topic | Status | Notes |
|-------|--------|-------|
| Documented intent | PARTIAL | Nightly SQL + media guidance |
| RPO / RTO | GAP | Undefined |
| Restore runbook | GAP | “Prefer restore” without step-by-step verification |
| Wrong path risk | GAP | Backing up `/uploads` misses live photos |

---

## 25. CI/CD

| Status | Notes |
|--------|-------|
| GAP | No `.github/workflows` (or other CI) in repo |
| Secret scanning | Recommended in SECURITY.md — not implemented |

---

## 26. Dependencies

| Item | Status | Notes |
|------|--------|-------|
| MailKit 4.15.1 | BLOCKED | NU1902 moderate advisory remains |
| SixLabors.ImageSharp | INFORMATIONAL | License warning at build; commercial notice needed |
| npm | INFORMATIONAL | No automated audit in CI |

---

## 27. Privacy

| Topic | Status | Notes |
|-------|--------|-------|
| ID encryption | PASS | Encrypted at rest; role-gated full reveal |
| Photos private | PASS | Auth media |
| Retention / anonymization / export rights | GAP | PRIVACY.md aspirational; not implemented |
| Key rotation | BLOCKED | Human ops B-003 |

---

## 28. Observability

| Topic | Status | Notes |
|-------|--------|-------|
| Custom health | PARTIAL | `/api/system/health` DB ping — not ASP.NET HealthChecks / readiness split |
| Correlation ID | GAP | Absent |
| Structured logging | PARTIAL | ILogger used; no standard enrichment |
| PII in logs | PARTIAL | Needs ongoing discipline; no automated redaction |

---

## 29. Product Architecture

| Topic | Status | Notes |
|-------|--------|-------|
| Core vs visitor module | PARTIAL | Docs clearer than code boundaries |
| VisitorService god-object | GAP | Recommended future split: Registration, VisitLifecycle, Pass, Face, Dashboard |
| Audit product feature | GAP | Removed table; commercial customers often require immutable audit |
| Contractor module seed | INFORMATIONAL | Entitlement without product surface |
| README “production-ready” | GAP | Overclaims vs SECURITY caveats |

---

## 30. Reviewer Questions (skeptical)

1. What is the official data-access standard: EF, ADO.NET, or both — and who decides?
2. Why is ADO.NET used for pass numbers instead of a SQL Server SEQUENCE object?
3. Why does `PassCode` uniqueness remain global rather than `(TenantId, PassCode)`?
4. How do you prove no SQL injection in seeder Raw SQL forever?
5. What happens if two Reception desks check in the same visit simultaneously?
6. What happens if pass allocation exhausts the series mid-shift?
7. How do you reprocess failed NotificationOutbox rows after SMTP outage?
8. Why is reminder scheduling based on `DateTime.Now` instead of tenant timezone?
9. Why does the reminder worker use the first tenant’s `ReminderHoursBefore` for all?
10. How is public branding multi-tenant on the login page?
11. Where is the immutable audit trail after DropAuditLogs?
12. How do you restore visitor photos if ops followed the `/uploads` backup section?
13. What are RPO and RTO for a customer site?
14. Why is there no CI pipeline enforcing tests and secret scans?
15. Is MailKit GHSA-9j88 accepted risk with a signed waiver?
16. How is ImageSharp licensed for commercial distribution?
17. Why are JWTs in localStorage rather than httpOnly cookies (tradeoffs)?
18. How do you rotate `Security:DataProtectionKey` without destroying ID ciphertext?
19. Is license expiry meant to be fail-closed 403 or read-only as UPGRADES says?
20. How do you version breaking API changes without `/api/v1`?
21. What is the maximum supported visitors/day and report size?
22. Why is Feedback listed in some review prompts but absent from the product?
23. How complete is Tamil coverage — which screens are still English-only?
24. How do you prevent cross-tenant leakage in static Settings cache under load?
25. Does `Task.Run` checkout email survive IIS app-pool recycle?
26. How is face recognition accuracy/threshold validated in production?
27. Who can call VerifyVisitor checkout mode — is that intentional?
28. Are EF migrations applied automatically on every IIS start in Production?
29. How do you detect and alert on degraded health beyond a JSON ping?
30. What proves host cannot approve another host’s visit under concurrent load?
31. How are orphaned files under `App_Data/media` cleaned?
32. What is the retention policy for NotificationOutbox bodies (PII)?
33. How do you prevent double face-enrol templates for the same visitor?
34. Is Swagger reachable in Production IIS?
35. What is the rollback plan if `AddPassNumberSeries` migration fails mid-deploy?

---

## 31. Priority Matrix

| ID | Pri | Severity | Category | Evidence | Current behavior | Risk | Recommended solution | Complexity | Dependencies |
|----|-----|----------|----------|----------|------------------|------|----------------------|------------|--------------|
| E-001 | P1 | HIGH | DEVOPS | No `.github` | Manual builds | Regressions ship unnoticed | Add CI: build, test, secret scan | M | — |
| E-002 | P1 | HIGH | DEPENDENCY | MailKit NU1902 | Advisory open | Supply-chain / SMTP risk | Track upstream; temp mitigate; waiver | S–M | Vendor |
| E-003 | P1 | HIGH | DEPLOYMENT | DEPLOYMENT.md `/uploads` | Wrong backup target | Photo loss on restore | Fix docs + backup scripts to `App_Data/media` | S | Ops |
| E-004 | P1 | HIGH | BACKEND | `DateTime.Now` in reminders/visits | Local server clock | Wrong reminder windows / visit days | Tenant TZ strategy; prefer UTC store + local display | M | Product |
| E-005 | P1 | HIGH | CONCURRENCY | **Fixed** — `RowVersion` + 409 | See `VISIT_CONCURRENCY_REVIEW.md` | Was double check-in race | Done | — | Tests |
| E-006 | P1 | MEDIUM | SECURITY | JWT in web storage | XSS → token theft | Session hijack | Harden CSP/XSS; consider BFF/httpOnly | M–L | UX |
| E-007 | P1 | HIGH | PRIVACY | B-003 key rotation | Key fixed | Future compromise / compliance | Versioned keys + re-encrypt plan | L | Ops |
| E-008 | P1 | MEDIUM | NOTIFICATIONS | No outbox retry worker | Failed email stays failed | Missed thank-you / reminders | Hosted outbox processor with backoff | M | — |
| E-009 | P1 | MEDIUM | MULTI-TENANT | ReminderHours first tenant | Shared offsets | Wrong schedule per tenant | Read setting inside tenant scope | S | — |
| E-010 | P1 | MEDIUM | PRODUCT | AuditLogs removed | Limited trail | Compliance gap | Reintroduce append-only audit or event store | M | Product |
| E-011 | P2 | MEDIUM | DATABASE | Seeder DDL Raw | Schema patch at seed | Drift vs migrations | Remove EnsureColumnWidths from seed | S | — |
| E-012 | P2 | MEDIUM | PERFORMANCE | Reports Take(5000) | Full graph load | Memory pressure | Streaming / async job | M | — |
| E-013 | P2 | MEDIUM | FRONTEND | i18n ~3 pages | Mostly English | Incomplete Tamil delivery | Expand keys; ban hardcoded in CI | M | — |
| E-014 | P2 | MEDIUM | API | No URL versioning | `/api/*` | Breaking clients | Introduce `/api/v1` when needed | M | Clients |
| E-015 | P2 | LOW | DATABASE | AdoSql no timeout | Default command timeout | Hung requests | Set CommandTimeout | S | — |
| E-016 | P2 | MEDIUM | ARCHITECTURE | Fat VisitorService | 1420 lines | Change risk | Split modules | L | — |
| E-017 | P2 | MEDIUM | DOCS | Stale DATABASE/API/README | Wrong seeds/ports/paths | Operator mistakes | Doc sync pass | S | — |
| E-018 | P2 | MEDIUM | BACKUP | No RPO/RTO | Undefined | Contract risk | Define with customer | S | Sales/Ops |
| E-019 | P2 | LOW | OBSERVABILITY | No correlation ID | Hard to trace | Slow incident response | Middleware + log enrich | S | — |
| E-020 | P2 | MEDIUM | PRIVACY | No retention/export | Aspirational PRIVACY.md | Compliance | Implement or narrow claims | L | Legal |
| E-021 | P3 | LOW | UX | A11y label wiring | Inconsistent | A11y debt | htmlFor/id pass | S | — |
| E-022 | P3 | LOW | PWA | No SW | Online banner only | Expectation mismatch | Drop claim or implement | M | — |
| E-023 | P3 | LOW | PRODUCT | Push stub | Logs only | Not customer push | Real FCM/APNs later | L | — |
| E-024 | P3 | INFORMATIONAL | LICENSE | ImageSharp warning | Build noise | Commercial license | Purchase/configure | S | Legal |
| E-025 | P0 | — | — | — | No open P0 confirmed this audit | — | Re-evaluate after CI/backup/race fixes | — | — |

**SMTP password rotation (B-002)** remains an **operational HIGH** outside code.

---

## 32. Architecture Sanity Check

| Question | Verdict |
|----------|---------|
| Too simple? | No — tenancy, entitlements, media isolation are real |
| Over-engineered? | Mildly in productization scaffold vs shipped modules; acceptable |
| Microservices needed? | **No** evidence |
| EF + selective ADO.NET? | **Yes — correct** |
| Minimum next architecture | Keep monolith; split VisitorService; durable outbox; timezone; CI; audit decision |

---

## 33. Recommended Implementation Order

1. Fix backup/docs paths + define RPO/RTO (E-003, E-018)  
2. Add CI build/test/secret scan (E-001)  
3. ~~Visit check-in/out concurrency protection (E-005)~~ **Done** — `VISIT_CONCURRENCY_REVIEW.md`  
4. Per-tenant reminder settings + UTC/timezone policy (E-004, E-009)  
5. Outbox retry worker (E-008)  
6. MailKit advisory tracking / waiver (E-002)  
7. Audit trail product decision (E-010)  
8. Expand Tamil coverage (E-013)  
9. Report streaming / caps (E-012)  
10. VisitorService modularization (E-016)

---

## Final Status Counts

| Metric | Count |
|--------|-------|
| **TOTAL FINDINGS** (matrix rows E-001–E-024; E-025 placeholder) | **24** actionable + blockers referenced |
| **P0** | **0** confirmed open criticals this pass |
| **P1** | **10** (E-001–E-010) |
| **P2** | **10** (E-011–E-020) |
| **P3** | **4** (E-021–E-024) |
| **PASS** (areas broadly healthy) | ~18 theme-level PASSes |
| **GAP** | ~22 theme-level GAPs |
| **PARTIAL** | ~20 |
| **BLOCKED** | 3 overnight (SMTP rotate, key rotate, MailKit) |
| **NOT APPLICABLE** | Feedback module; full SaaS |

### Highest-risk findings

1. **E-003** — Backup docs miss live media path (data-loss on restore)  
2. **E-001** — No CI  
3. **E-004 / E-009** — Timezone + reminder tenant setting bugs  
4. **E-002 / B-004** — MailKit advisory still open  
5. **E-006** — JWT in web storage  

### Top 10 next implementation tasks

1. Correct IIS/backup documentation and scripts for `App_Data/media`  
2. Introduce CI (build + `dotnet test` + secret scan)  
3. ~~Add visit optimistic concurrency / conditional status transitions~~ **Done (E-005)**  
4. Tenant-scoped reminder configuration + UTC strategy  
5. Notification outbox background retry  
6. Decide/reintroduce commercial audit logging  
7. Expand i18n beyond Login/Settings/Inside  
8. Align `UPGRADES.md` license behavior with `EntitlementService`  
9. Bound/stream report exports  
10. Track MailKit advisory to closure or formal risk acceptance  

---

*End of audit. No application source was modified; only this document and `DATA_ACCESS_AUDIT.md` were authored.*
