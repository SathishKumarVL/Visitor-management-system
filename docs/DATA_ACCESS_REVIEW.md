# Data Access Review

Review comment: “Change raw scripting to ADO.”

Interpretation: use **ADO.NET (`Microsoft.Data.SqlClient`)** for justified direct SQL — **not** a wholesale replacement of EF Core.

Primary ORM remains **EF Core** for CRUD and migrations.

## Inventory

| File | Class/Method | Current approach | Purpose | Security risk | Performance consideration | Recommended approach | Action taken |
|------|--------------|------------------|---------|----------------|---------------------------|----------------------|--------------|
| `Services/VisitorService.cs` | `AcquireNumberAllocationLockAsync` | `ExecuteSqlRawAsync` + `{0}` param | `sp_getapplock` for visit/visitor number serialization | Low — parameterized; resource embeds server-side tenant id | Fine | Keep EF Raw **or** move to ADO.NET helper for consistency with pass allocation | Converted to ADO.NET (`AdoSql`) with `SqlParameter` |
| `Data/DbSeeder.cs` | `BackfillTenantIdsAsync` | `ExecuteSqlRawAsync` multi-UPDATE | One-time legacy tenant backfill | Low — parameterized tenant Guid | Bulk UPDATE appropriate | Keep EF Raw (seed-only) | Documented; left as EF |
| `Data/DbSeeder.cs` | `BackfillSiteIdsAsync` | `ExecuteSqlRawAsync` | Site backfill for one tenant | Low — parameterized | Bulk UPDATE appropriate | Keep EF Raw | Documented; left as EF |
| `Data/DbSeeder.cs` | `EnsureColumnWidthsAsync` | `ExecuteSqlRawAsync` static DDL | Local schema patch | None — no user input | N/A | Prefer real migrations long-term; keep seed DDL for now | Documented; left as EF |
| `Data/Migrations/20260915033644_TenantScopedMastersAndIsolation.cs` | `Up` | `migrationBuilder.Sql` hardcoded GUIDs | Historical master TenantId fix | None — migration constants | N/A | Leave migration SQL | No change |
| `Services/PassNumberService.cs` | `AllocateNextAsync` | **New** ADO.NET | Atomic pass sequence allocate | Mitigated — parameterized; `TenantId` from `ITenantContext` | Row lock + OUTPUT | ADO.NET required for atomic UPDATE…OUTPUT | Implemented |
| Reporting / masters / visitors CRUD | — | EF Core | Normal entity access | EF filters + auth | — | Keep EF Core | No change |

## Package

- Direct reference: `Microsoft.Data.SqlClient`
- Do **not** use `System.Data.SqlClient`

## Rules for new ADO.NET

1. Always `SqlParameter` — never concatenate user input into SQL.
2. Always filter `TenantId = @TenantId` from authenticated `ITenantContext` (never from request headers/body).
3. Do not log parameter values that may contain PII.
4. Prefer EF Core for ordinary CRUD.

## Tests

See `backend.Tests/AdoDataAccessTests.cs` and `PassNumberAllocationTests.cs`.
