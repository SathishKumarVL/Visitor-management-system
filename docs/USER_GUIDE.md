# User Guide — TIAANO VMS

## Reception tablet

1. Login as `reception`
2. Use **Reception Mode** large buttons
3. **New Visitor** → complete the step-based wizard (under ~2 minutes)
4. If your site does not require approval, the visitor is checked in and the pass prints straight away
5. If your site requires approval, reception sees a "waiting for host approval" message instead —
   check in once the host has approved
6. Use **Currently Inside** / **Check Out** as visitors leave. Currently Inside can be searched by
   name, company, host, location or visit number, and sorted by time on site
7. **Expected Visitors** → Check In when pre-registered guests arrive

## Security tablet

1. Login as `security`
2. **Verify Visitor** by Visit Number printed on the pass
3. Confirm photo/name/host and check in or check out as needed
4. Use **Currently Inside** and search for name/mobile/company lookups

## Host

1. Login as `host`
2. Open **Pending Approvals** to see visits waiting on a decision, with photo, company, purpose and visit number
3. **Approve** — reception can then check the visitor in
4. **Reject** — a reason is required and is recorded against the visit

A rejected visit can never be checked in. If your site has approval turned off in Settings,
this queue stays empty because visits are cleared to enter on registration.

## Emergency

1. Login as `security` (or reception/admin) and open **Emergency Mode**
2. The roster lists everyone currently inside with photo, company, host, location and entry time
3. Mark each person **Verified**, **Evacuated** or **Missing** — marks are saved on the server,
   so every marshal sees the same counts, and each one is audited
4. Correcting a mark adds a new record rather than erasing the old one

Employee presence is **not** tracked by this system. The roster covers visitors only and says so;
account for staff using your existing muster process.

## Admin

- Dashboard statistics & charts
- Users, departments, hosts, purposes, locations
- Settings (logo path, approval/photo requirements, long-stay warning). **Approval required** and
  **Walk-in approval required** are separate switches, so scheduled visits and walk-ins can be
  gated independently
- Reports (Excel / PDF / CSV)
- Audit log (read-only)

## Visitor pass

Printed pass includes branding, photo, visitor/host/department/purpose/location, human-readable Visit Number (e.g. `VMS-2026-000184`), check-in time, and status. No QR codes.
