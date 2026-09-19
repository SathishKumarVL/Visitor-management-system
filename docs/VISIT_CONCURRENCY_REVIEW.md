# Visit concurrency review

**Status:** Implemented (P1 E-005)  
**Date:** 2026-09-18  
**Scope:** Optimistic concurrency for `VisitorVisit` lifecycle writes (check-in, check-out, approve, reject)

---

## 1. Visit state model

Existing enum (`VisitStatus`) — unchanged:

| Status | Meaning |
|--------|---------|
| Expected | Pre-registered appointment |
| PendingApproval | Awaiting host decision |
| Approved | Cleared for check-in |
| Rejected | Host rejected |
| Cancelled | Superseded expected visit / cancelled |
| Inside | Checked in on site |
| CheckedOut | Left site |

**Server-side transition gates (preserved):**

- Check-in allowed from: `Approved`, or `Expected` when tenant approval is off  
- Check-in blocked from: `Inside`, `CheckedOut`, `Rejected`, `Cancelled`, `PendingApproval`, `Expected` when approval required  
- Check-out allowed only from: `Inside`  
- Duplicate sequential check-in / check-out rejected with business `400` messages (`EnsureCheckInAllowed` / `EnsureCheckOutAllowed`)

---

## 2. Concurrency problem

Before this change, check-in/out used:

1. Load visit  
2. Validate status  
3. Mutate + `SaveChangesAsync`

Two simultaneous requests could both pass the status guard while both still saw the pre-transition status, then both commit. Effects included duplicate active passes / print increments and ambiguous timestamps.

---

## 3. Chosen solution

**EF Core + SQL Server `rowversion` optimistic concurrency** on `VisitorVisit.RowVersion`.

- Not `DateTime` tokens  
- Not homemade compares  
- Not pessimistic / distributed locks  

On conflict EF throws `DbUpdateConcurrencyException`, translated to `ConcurrencyConflictException` → **HTTP 409** with a safe message.

---

## 4. Database implementation

- Property: `byte[] RowVersion` with `[Timestamp]`  
- Fluent: `e.Property(x => x.RowVersion).IsRowVersion()`  
- Migration: `20260918174905_AddVisitRowVersion`  
  - Adds non-nullable `rowversion` column to `VisitorVisits`  
  - Existing rows remain valid (SQL Server assigns versions)  
  - No table rebuild, no data delete  

---

## 5. API behavior

| Outcome | HTTP | Body |
|---------|------|------|
| Success check-in/out | 200 | Existing `ApiResponse` |
| Invalid transition | 400 | Business message |
| Stale concurrent update | **409** | `"The visit was modified by another user. Refresh the visit and try again."` |
| Cross-tenant visit id | 400 | Visit not found (tenant filter) |

Mapped in:

- `VisitorsController` check-in/out  
- `ApprovalsController` approve/reject  
- `ExceptionMiddleware` (`ConcurrencyConflictException` and `DbUpdateConcurrencyException` safety net)

No stack traces, SQL, or rowversion bytes in responses.

---

## 6. Frontend behavior

- `apiErrorMessage` surfaces the 409 message (with fallback text)  
- `isConflictError` helper  
- Visitor detail / approvals / face checkout: show message and **refresh** local list/detail — **no blind retry** of the mutating call  

---

## 7. Transaction behavior

Check-in updates visit + pass in a **single** `SaveChangesAsync` (implicit transaction).  
Check-out updates visit + deactivates passes in one save.  
Thank-you email remains **after** commit via background `Task.Run` (unchanged; delivery stays outside the DB transaction).

No extra explicit transactions were added; atomicity of the business state change was already a single save.

---

## 8. Idempotency behavior

| Pattern | Protection |
|---------|------------|
| Sequential duplicate check-in/out | Status validation → 400 |
| Concurrent duplicate check-in/out | RowVersion → 409 (or 400 if the loser reloads after commit) |

No separate idempotency-key framework; status + rowversion is sufficient for this lifecycle.

---

## 9. Test coverage

`backend.Tests/VisitConcurrencyTests.cs`:

- Happy-path check-in / check-out  
- Invalid check-out when not inside  
- Stale `RowVersion` → `DbUpdateConcurrencyException`  
- Parallel HTTP check-in/out → one success, other controlled 409/400, never 500  
- Service-level parallel check-in → controlled exception  
- Safe conflict message (no SQL leakage)  
- Tenant A cannot check-in Tenant B visit  

Existing `CoreWorkflowTests` lifecycle tests remain the primary transition suite.

---

## 10. Write paths reviewed

| Path | Uses RowVersion? | Notes |
|------|------------------|-------|
| `CheckInAsync` / `CheckOutAsync` | Yes | Via `SaveVisitLifecycleAsync` |
| `ApproveAsync` / `RejectAsync` | Yes | Same helper |
| `ReprintPassAsync` | N/A if visit unmodified | Pass-only updates; visit token not required unless visit entity is marked modified |
| `EmergencyService.RecordRollCallAsync` | N/A | Append-only events; does not mutate visit status |
| Registration / expected create | Insert | New rows; concurrency token assigned by SQL Server |
| Expected cancel on arrival | Direct `SaveChanges` | Rare; not dual-desk lifecycle race focus |
| Pass number ADO | Separate | Already atomic; does not bypass visit rowversion for status |
| Seeder status flips | Startup only | Acceptable |

---

## 11. Remaining limitations

- True 409 vs 400 for the losing concurrent request depends on timing (loser may reload and hit status guard). Both are safe; neither is 500.  
- Pass reprint print-count races are out of scope.  
- Approval double-submit is covered by the same token when both update the visit row.  
- Client does not send `RowVersion` in the request body; concurrency is server-side between concurrent writers (correct for this desk workflow).

---

## 12. Verdict

**P1 visit check-in/check-out concurrency: Fixed — READY FOR REVIEW.**
