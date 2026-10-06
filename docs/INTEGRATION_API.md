# Integration API (for other applications)

Use this API to feed employee details into the Visitor Management System (VMS) from an HR / payroll /
ERP system, and to pull visitor reports out of it.

## 1. What you need

| Item | Value |
|------|-------|
| Base URL | `https://visitors.tiaanosoft.com/api/integration` |
| Authentication | Header `X-Api-Key: <key>` on every request |
| Format | JSON (`Content-Type: application/json`) |
| Transport | HTTPS only |

There is no login, token or session: the API key alone identifies the calling application.

## 2. Getting an API key

1. Sign in to VMS as **Super Admin**.
2. Go to **Settings → API keys** and create a key.
   - Give it a name that identifies the caller, e.g. `HRMS employee sync`.
   - Tick only the scopes it needs:

     | Scope | Allows |
     |-------|--------|
     | `employees` | `GET /employees`, `POST /employees` |
     | `reports` | `GET /reports/visitors` |

     `GET /masters` works with any valid key.
3. Copy the key immediately. It is shown **once**; VMS only stores a hash of it. The same window shows the
   API URL and each endpoint the key can use, with copy buttons; **Copy all details** copies the URL, header
   and endpoints in one go. The API URL is also shown at the top of the API keys page at any time.
4. Hand the key to the other application's team over a secure channel (not plain email or chat).

To stop an application, **Revoke** its key on the same page; it stops working at once. If a key may have
leaked, revoke it and create a new one. "Last used" on that page shows whether a key is in use.

## 3. Responses and errors

Every JSON response has the same envelope:

```json
{ "success": true, "data": { }, "message": null, "errors": null }
```

| HTTP status | Meaning |
|-------------|---------|
| 200 | Request handled. For `POST /employees`, check `success` and `data.errors` — some rows can fail while others save. |
| 400 | Request body or parameters are invalid; `message` explains why. |
| 401 | Missing, wrong or revoked API key. |
| 403 | The key is valid but does not have the scope for this endpoint. |

## 4. Endpoints

### 4.1 `GET /masters` — locations and departments

Use the returned names or codes when sending employees.

```bash
curl -H "X-Api-Key: $VMS_KEY" https://visitors.tiaanosoft.com/api/integration/masters
```

```json
{
  "success": true,
  "data": {
    "locations":   [ { "id": "…", "name": "Tiaano HQ",    "code": "L-HQ",  "active": true } ],
    "departments": [ { "id": "…", "name": "Human Resources", "code": "HQ-HR", "active": true } ]
  }
}
```

### 4.2 `POST /employees` — create or update employees (scope `employees`)

Send one or many employees (up to **5000** per request). Each employee is matched on `code`:

- `code` not in VMS yet → the employee is **created**.
- `code` already in VMS → the employee is **updated** with the values sent.

```bash
curl -X POST https://visitors.tiaanosoft.com/api/integration/employees \
  -H "X-Api-Key: $VMS_KEY" -H "Content-Type: application/json" \
  -d @employees.json
```

```json
{
  "employees": [
    {
      "code": "EMP1001",
      "fullName": "Suresh Baskar",
      "department": "HQ-HR",
      "location": "L-HQ",
      "designation": "HR Manager",
      "email": "suresh.baskar@tiaano.com",
      "phone": "9876543210",
      "intercom": "2101",
      "active": true
    }
  ]
}
```

| Field | Required | Notes |
|-------|----------|-------|
| `code` | Yes | Employee code / ID from your system. The match key — keep it stable. Max 50 characters. |
| `fullName` | Yes | Max 200 characters. |
| `department` | Yes | Department **name or code**. It must already exist in VMS (see `/masters`). |
| `location` | No | Location name or code. Omit to leave it unchanged; a new employee without a location is visible at every location. |
| `designation`, `phone`, `email`, `intercom` | No | `email` is used for visitor check-in / check-out / appointment mails, so send it whenever you have it. |
| `active` | No | Send `false` when an employee leaves; they can then no longer be selected as a host. Omit to leave unchanged (new employees default to active). |

Employees are never deleted through the API; deactivate them with `"active": false`.

Response:

```json
{
  "success": false,
  "data": {
    "total": 3, "created": 1, "updated": 1, "failed": 1,
    "errors": [ { "index": 2, "code": "EMP1003", "message": "Department 'XYZ' was not found." } ]
  },
  "message": "1 of 3 employee(s) could not be saved."
}
```

Each employee succeeds or fails on its own. `index` is the zero-based position in your `employees` array.
`success` is `true` only when every row saved. Fix the failed rows and send them again — re-sending rows that
already saved is safe (they are simply updated).

**Recommended sync:** send the full employee list once, then on a schedule (e.g. nightly) send new and changed
employees, with `"active": false` for leavers.

### 4.3 `GET /employees` — read employees back (scope `employees`)

```bash
curl -H "X-Api-Key: $VMS_KEY" "https://visitors.tiaanosoft.com/api/integration/employees?includeInactive=true"
```

Returns `code`, `fullName`, `designation`, `department`, `departmentCode`, `location`, `locationCode`,
`phone`, `email`, `intercom` and `active` for each employee. Inactive employees are included only with
`includeInactive=true`.

### 4.4 `GET /reports/visitors` — visitor report (scope `reports`)

The same data as **Reports** in the application.

```bash
curl -H "X-Api-Key: $VMS_KEY" \
  "https://visitors.tiaanosoft.com/api/integration/reports/visitors?dateFrom=2026-10-01&dateTo=2026-10-05&location=L-HQ"
```

| Parameter | Notes |
|-----------|-------|
| `dateFrom`, `dateTo` | `yyyy-MM-dd`. Default: the last 30 days. |
| `reportType` | `daily` (all visits, default), `inside`, `entryexit`, `rejected`, `manualcheckouts`, `defaulters`. |
| `location` | Location name or code (or `locationId`). |
| `departmentId`, `hostEmployeeId`, `purposeId`, `company`, `status` | Optional filters. |
| `page`, `pageSize` | JSON only. Without them all rows are returned (max 5000). |
| `sortBy`, `sortDir` | e.g. `sortBy=checkin&sortDir=desc`. |
| `format` | `json` (default), `csv` or `excel` (file download). |

## 5. Quick test

Replace the key and run from any machine that can reach the site:

```powershell
$h = @{ "X-Api-Key" = "<your key>" }
Invoke-RestMethod -Headers $h https://visitors.tiaanosoft.com/api/integration/masters
```

A `401` means the key is wrong or revoked; a `403` means it lacks the scope for that endpoint.
