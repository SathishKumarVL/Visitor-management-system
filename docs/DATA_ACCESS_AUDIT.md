# Data Access Audit

**Scope:** TIAANO VMS backend — EF Core, ADO.NET, raw SQL.  
**Mode:** Audit only (no code or schema changes).  
**Date:** 2026-09-18  
**Related:** `docs/DATA_ACCESS_REVIEW.md`, `docs/PASS_NUMBER_REVIEW.md`

---

## 1. Recommended data-access standard (documented, not implemented)

| Technology | Use for | Do not use for |
|------------|---------|----------------|
| **EF Core** | CRUD, relationships, LINQ, most reports, migrations, tenant query filters | Provider-specific atomic SQL that EF cannot express safely |
| **ADO.NET (`Microsoft.Data.SqlClient`)** | Atomic counters (`UPDATE…OUTPUT`), `sp_getapplock`, justified bulk/SPs | Ordinary entity CRUD |
| **Raw SQL via EF `ExecuteSqlRaw`** | Seed/migration backfills only when parameterized | Request-path user input |
| **String-concatenated SQL** | Never | — |

**Project status:** Standard is **implicit** (recent sprint docs + code). No single `DATA_ACCESS.md` policy enforced in CI.

---

## 2. Technology inventory

| Technology | Present? | Evidence |
|------------|----------|----------|
| EF Core + SQL Server | Yes | `Program.cs` `UseSqlServer`; entities; migrations |
| Microsoft.Data.SqlClient (direct) | Yes | `Tiaano.Vms.Api.csproj` 5.2.2; `AdoSql.cs`, `PassNumberService.cs` |
| System.Data.SqlClient | No | — |
| Dapper | No | — |
| FromSqlRaw / FromSqlInterpolated | No | Repo search empty |
| ExecuteSqlInterpolated | No | — |
| ExecuteSqlRawAsync | Yes (seeder only) | `DbSeeder.cs` |
| Stored procedures (app-owned) | Only `sp_getapplock` (system) | `VisitorService.AcquireNumberAllocationLockAsync` |
| Views / triggers / functions (app) | None found in migrations beyond indexes/FKs | — |
| migrationBuilder.Sql | Yes (historical) | `20260915033644_TenantScopedMastersAndIsolation.cs` |

---

## 3. Occurrence table

| File | Class | Method | Technology | Purpose | Raw SQL? | ADO.NET? | EF Core? | Parameterized? | Tenant scoped? | Transaction? | Risk | Recommendation | Priority |
|------|-------|--------|------------|---------|----------|----------|----------|----------------|----------------|--------------|------|----------------|----------|
| `Services/AdoSql.cs` | `AdoSql` | helpers | ADO.NET | Shared param helpers | Text | Yes | No | Enforced by API | Caller responsibility | Caller | Low if callers correct | Add `CommandTimeout`; document ownership | P2 |
| `Services/PassNumberService.cs` | `PassNumberService` | `AllocateNextAsync` / bump SQL | ADO.NET | Atomic pass sequence | Yes | Yes | Mixed (EF for admin CRUD) | Yes `@Id` `@TenantId` | Yes `TenantId` from `ITenantContext` | Yes `SqlTransaction` | Low | Keep; add timeout; consider unique series constraints | P2 |
| `Services/VisitorService.cs` | `VisitorService` | `AcquireNumberAllocationLockAsync` | ADO.NET | `sp_getapplock` | Yes | Yes | Uses EF ambient connection/tx | Yes `@Resource` | Lock name embeds tenant | Ambient EF tx | Low | Keep; set lock timeout visibility | P3 |
| `Data/DbSeeder.cs` | `DbSeeder` | `BackfillTenantIdsAsync` | EF Raw | Legacy TenantId backfill | Yes | No | Yes | Yes `{0}` | Writes tenant id | Seeder | Low | Keep seed-only; prefer migrate away | P3 |
| `Data/DbSeeder.cs` | `DbSeeder` | `BackfillSiteIdsAsync` | EF Raw | SiteId backfill | Yes | No | Yes | Yes | `WHERE TenantId={0}` | Seeder | Low | Keep | P3 |
| `Data/DbSeeder.cs` | `DbSeeder` | `EnsureColumnWidthsAsync` | EF Raw | Static DDL patch | Yes | No | Yes | N/A | Schema | Seeder | Med maintainability | Fold into migrations; remove from seed | P2 |
| Migration `…TenantScopedMasters…` | Migration | `Up` | migration SQL | One-time GUID assign | Yes | No | Migration | Hardcoded constants | Assigns default tenant | Migration | None | Leave historical | N/A |
| Controllers/Services (most) | various | CRUD | EF LINQ | Domain | No | No | Yes | N/A (LINQ) | Global filters + claims | Mixed | Baseline | Keep EF | — |
| `MasterAndReportServices` | `ReportService` | Generate/Export | EF Include + ToList | Reports ≤5000 | No | No | Yes | N/A | Filtered | No explicit tx | Perf | Streaming/async later | P2 |

---

## 4. SQL injection

| Finding | Status | Evidence |
|---------|--------|----------|
| User-controlled string concat into SQL | **Not found** on request paths | Pass/applock/seeder use parameters or constants |
| Dynamic ORDER BY / table names from input | **Not found** | — |
| Interpolated `$"…{user}…"` SQL | **Not found** in services | — |
| Dev DbUpdateException detail | Dev-only message leak | `ExceptionMiddleware` — not injection | INFORMATIONAL |

**Verdict:** No confirmed exploitable SQL injection path in current backend SQL call sites.

---

## 5. Parameterization & tenant scope (ADO)

| Call site | Parameters | Tenant source |
|-----------|------------|---------------|
| Pass allocation UPDATE | `@Id`, `@TenantId` | `ITenantContext` (throws if missing) |
| Applock | `@Resource` | Resource string includes server-side tenant id |
| Seeder Raw | `{0}`/`{1}` EF params | Seed WellKnown tenant |

**Rule:** ADO must not rely on EF global query filters — **pass allocation complies**.

---

## 6. Transactions & connection management

| Area | Behavior | Gap |
|------|----------|-----|
| Pass allocate | `BeginTransactionAsync` + commit/rollback | No `CommandTimeout` on `AdoSql` commands |
| Visit number | EF strategy + applock inside ambient tx | Complex but tested |
| Registration | Multiple `SaveChanges` steps | Partial failure windows possible (visit saved then photo fails) — see enterprise audit |
| Checkout thank-you | `Task.Run` new scope | Fire-and-forget; not transactional with checkout |
| AdoSql reader | Caller must dispose reader/cmd | `ExecuteReaderAsync` does not wrap `await using` on command — ownership subtlety | P2 |

---

## 7. Async

ADO and EF paths examined use `*Async` APIs. No widespread sync-over-async found on DB calls in services.

---

## 8. Query performance (EF)

| Area | Pattern | Risk |
|------|---------|------|
| Dashboard | Projections + in-memory grouping | Acceptable at current scale |
| Reports | `Include` graph + `Take(5000).ToListAsync()` | Memory / latency under growth |
| Split query | Enabled globally | Mitigates cartesian Include blow-ups |
| Soft delete | `IsActive` flags | Must remember filters in reports |

---

## 9. Schema notes affecting data access

| Item | Note |
|------|------|
| `IX_VisitorPasses_PassCode` | **Globally unique** (not per-tenant) — correct for globally unique codes; must never put reusable badges here |
| Pass series counters | Concurrency via conditional UPDATE, not `RowVersion` on visit |
| `VisitorVisit` | No optimistic concurrency token — check-in races rely on status checks + last-write |

---

## 10. Files/methods requiring future attention

1. `AdoSql.cs` — add default `CommandTimeout`; harden reader disposal pattern.  
2. `PassNumberService.cs` — document/enforce non-overlapping allocations in admin upsert.  
3. `DbSeeder.EnsureColumnWidthsAsync` — remove DDL from runtime seed.  
4. `ReportService` — bounded streaming for large exports.  
5. Explicit project coding standard + CI check forbidding `ExecuteSqlRaw` with string concat (policy).

---

## 11. Summary

| Metric | Count |
|--------|-------|
| Justified ADO.NET sites | 2 (pass allocate, applock) |
| EF Raw (seed) | 3 |
| Injection GAPs | 0 confirmed |
| Missing project-wide written standard in CI | GAP (docs exist partially) |

**Architecture sanity:** Hybrid EF + thin ADO is **appropriate** for this modular monolith. Do **not** convert CRUD to ADO.NET.
