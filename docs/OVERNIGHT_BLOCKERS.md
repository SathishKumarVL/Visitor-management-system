# Overnight Blockers

Only genuine human/external/destructive blockers. No routine coding decisions.

## B-001 — Git history object prune (reflog GC)

**Phase:** 1A  
**Severity:** Medium (local only; no remote)  
**Why blocked:** Aggressive `git reflog expire` + `git gc --prune=now` was not confirmed via smart-mode approval during the prior session.  
**Impact:** Old secret-bearing commits may still exist as unreachable objects via reflog until pruned. They are not on any branch tip.  
**Safe mitigation already done:** Orphan rewrite; active history is secret-free; external bundle backup created.  
**Required human action:** Optionally run locally:

```powershell
git reflog expire --expire=now --all
git gc --prune=now --aggressive
```

**Do not** restore `v0.1.0-audit-baseline` into the active repo (contains secrets). Keep the external bundle offline/secure.

## B-002 — SMTP credential rotation

**Phase:** 1A  
**Severity:** High (operational)  
**Why blocked:** Requires mailbox administrator to rotate the previously exposed SMTP password.  
**Required human action:** Rotate SMTP password with mail admin; set `Smtp:Password` via User Secrets / environment; re-enable SMTP when ready.

## B-003 — PII encryption key operational rotation

**Phase:** 1A  
**Severity:** High (data integrity)  
**Why blocked:** Blindly rotating `Security:DataProtectionKey` would make existing `VisitorDocuments.IdNumberEncrypted` unreadable. Requires planned re-encryption migration.  
**Required human action:** Approve key-versioning migration window after Phase 1B/2 foundations; keep current key in secret store until then.

## B-004 — MailKit moderate advisory GHSA-9j88-vvj5-vhgr

**Phase:** 1B  
**Severity:** Medium  
**Why blocked:** Latest available MailKit 4.15.1 still reports NU1902. No clean patched package at time of run.  
**Mitigation:** SMTP TLS validation enforced; passwords not logged; outbox preserved. Revisit when upstream fixes land.
