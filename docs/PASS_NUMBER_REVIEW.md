# Pass Number Review

## Symptom

```
Cannot insert duplicate key row in object 'dbo.VisitorPasses'
with unique index 'IX_VisitorPasses_PassCode'.
The duplicate key value is (1).
```

## Root cause

1. Registration required a free-text **desk badge** (`VisitorVisit.PassNumber`).
2. On approve/register, the same string was copied into `VisitorPass.PassCode`.
3. `IX_VisitorPasses_PassCode` is a **global unique** index that never clears on checkout (`IsActive = false` only).
4. Reusing badge `"1"` (or any previously used PassCode) therefore fails uniqueness.

This is **not** an identity-column bug and **not** fixed by dropping the unique index.

## Design (implemented)

| Concept | Role |
|---------|------|
| `PassNumberSeries` | Tenant (optional site) sequence: Prefix, Start, End, Current |
| `PassNumberAllocation` | Optional per-user range within a series |
| `VisitorPass.PassCode` | Stable human-readable unique code, e.g. `VMS-2026-000184` |
| `VisitorVisit.PassNumber` | Same value for display / search (assigned once) |

- Numbers are allocated **atomically** via ADO.NET `UPDATE … OUTPUT` under a transaction (tenant-scoped).
- **Format:** `{Prefix}{Year}-{number:D6}` e.g. `VMS-2026-000184` (Year from series when set; otherwise current calendar year).
- Unique index on `PassCode` remains the final safety net.
- Exhausted ranges return a clear business error.
- Historical pass codes never change.

## Admin UI

Settings → Pass Settings: configure series prefix/range; optional user allocations.

## Concurrency

Two simultaneous allocations for the same series take the next distinct numbers; uniqueness prevents collisions.
