# TIAANO VMS Production Deployment Readiness

**Audit date:** 2026-09-19  
**Target site (declared):** https://visitors.tiaanosoft.com/  
**Scope:** Read-only readiness analysis. No upload, deploy, IIS, DNS, DB, or application code changes were performed.

**Statuses used:** `READY` · `READY AFTER CONFIGURATION` · `BLOCKED` · `HUMAN INPUT REQUIRED`

---

## 1. Current Build Status

| Artifact | Command / path | Status |
|----------|----------------|--------|
| Frontend build | `npm run build` → `frontend/dist` | **READY** (verified prior) |
| Backend Release build | `dotnet build backend -c Release` | **READY** (verified prior) |
| Backend publish | `dotnet publish backend -c Release -o .\deploy\backend` | **READY** (verified prior; no ImageSharp license gate) |
| ImageSharp | Removed; SkiaSharp 3.119.0 + NativeAssets.Win32 | **READY** |
| Face gate | 0 reject / 1 accept / 2+ reject (`MULTIPLE_FACES_DETECTED`) | **READY** (code + tests) |
| Visit concurrency | `RowVersion` migration + HTTP 409 path | **READY** (code; DB must apply migration) |
| MailKit NU1902 | Warning only | Does **not** technically block publish |

**Overall build package:** **READY**  
**Overall production go-live:** **READY AFTER CONFIGURATION** (secrets, IIS layout, CORS/hosts, SQL, face models)

---

## 2. Frontend Deployment

| Item | Finding |
|------|---------|
| Build output | `frontend/dist` (`index.html`, `assets/*`, branding references) |
| API client | `frontend/src/lib/api.ts` → `axios.create({ baseURL: '/api' })` |
| Compiled into Vite build? | **Yes** — relative `/api` is baked into the JS bundle |
| Dev proxy | `vite.config.ts` proxies `/api` and `/branding` → `http://127.0.0.1:5080` |
| `.env` files | **None** in repository |
| `VITE_API_ORIGIN` | Optional; used only by `assetUrl()` for absolute asset prefixes — **not** required if SPA and API share origin |
| Production API URL expected by current build | **Same origin:** `https://visitors.tiaanosoft.com/api/...` |
| Separate API host without rebuild? | **Not supported** by current bundle (would need reverse proxy or a new build with different base URL) |
| HTTPS | Required for production browser site; API calls are relative so they inherit the page scheme |

**Implication:** Frontend and API must appear on the **same host** (or IIS must reverse-proxy `/api` and `/branding` to the API).

**Frontend deploy status:** **READY AFTER CONFIGURATION** (IIS static site + `/api` proxy, or equivalent)

---

## 3. Backend Deployment

| Item | Finding |
|------|---------|
| Publish output | `deploy/backend` (`Tiaano.Vms.Api.dll`, `web.config`, `appsettings*.json`, runtimes, `wwwroot/branding`, …) |
| Hosting model | `web.config`: AspNetCoreModuleV2, **`hostingModel="inprocess"`** |
| Entry | `dotnet` → `.\Tiaano.Vms.Api.dll` |
| Environment | Must be **`ASPNETCORE_ENVIRONMENT=Production`** |
| Startup | Fails closed without `Jwt:Key` and `Security:DataProtectionKey` |
| Migrations | `DbSeeder.SeedAsync` calls `Database.MigrateAsync()` on every successful start |
| SPA serving | **API does not serve the React SPA.** `Program.cs` only maps static files for `/branding` from `wwwroot/branding`. Controllers handle `/api/*`. |
| Swagger | Development only |

**Note:** Older `docs/DEPLOYMENT_WINDOWS_SERVER.md` suggested copying `frontend/dist` into API `wwwroot`. That is **insufficient with current code** unless IIS serves those files outside ANCM or the app is later changed to serve SPA fallback. Prefer: **static site for SPA + reverse proxy `/api` (+ `/branding`) → API app**.

**Backend deploy status:** **READY AFTER CONFIGURATION**

---

## 4. Required Production Configuration

| Configuration | Required? | Current Source | Production Source | Secret? | Action Needed |
|---------------|-----------|----------------|-------------------|---------|---------------|
| `ASPNETCORE_ENVIRONMENT` | Yes | launchSettings = Development | IIS env / `web.config` | No | Set **Production** |
| `ConnectionStrings:DefaultConnection` | Yes | Placeholder in `appsettings.Production.json` | Env / server `appsettings.Production.json` / vault | **Yes** | Supply real SQL |
| `Jwt:Key` | Yes | **Absent** from committed Production JSON | Env / vault / server-only file | **Yes** | Create ≥32-byte key |
| `Jwt:Issuer` | Yes | `Tiaano.Vms` in appsettings | Same (safe) | No | Keep / confirm |
| `Jwt:Audience` | Yes | `Tiaano.Vms.Clients` | Same | No | Keep / confirm |
| `Jwt:AccessTokenMinutes` | Optional | 30 in base `appsettings.json` | Same | No | Optional tune |
| `Security:DataProtectionKey` | Yes | **Absent** from committed Production JSON | Env / vault | **Yes** | Create; **never rotate casually** (ID/SMTP ciphertext) |
| `Cors:Origins` | Yes (non-Dev) | `https://vms.tiaano.local` (placeholder) | Must include real origin | No | Set `https://visitors.tiaanosoft.com` |
| `AllowedHosts` | Yes | `vms.tiaano.local;localhost` | Real host(s) | No | Set `visitors.tiaanosoft.com` |
| `Smtp:*` (file) | Optional | Base disabled; Dev has host/user | Prefer DB Settings UI + encrypted password | Password **Yes** | Configure after first login or via secure config |
| `Smtp:IgnoreSslErrors` | Must be false in Production | Enforced by `SecretConfiguration` | — | No | Do not enable |
| `FaceRecognition:*` | Optional feature | Enabled in base appsettings | Same + models on disk | No | Deploy `MlModels` if face required |
| `FaceRecognition:ModelDirectory` | If face on | `MlModels` | Path under content root | No | Ensure ONNX files present |
| `Reminders:PollMinutes` | Optional | 5 | Same | No | Optional |
| `Seed:DefaultPassword` | First boot only | Not in committed files | Env (create-only) | **Yes** | Needed to create `superadmin`/`admin` if missing |
| `Logging:LogLevel` | Optional | Warning in Production JSON | Same | No | Optional |
| Frontend `baseURL` | Built-in | `/api` | Same-origin proxy | No | IIS layout |
| `VITE_API_ORIGIN` | No (same-origin) | Unused in current prod build | Only if assets on other origin | No | Usually skip |

### SAFE TO COMMIT vs SERVER-SIDE SECRET

| SAFE TO COMMIT (non-secret) | SERVER-SIDE SECRET (never commit) |
|-----------------------------|-----------------------------------|
| Issuer / Audience / token minutes | `Jwt:Key` |
| Cors origin list (public URLs) | `Security:DataProtectionKey` |
| FaceRecognition thresholds | SQL password / full connection string with credentials |
| Logging levels | `Seed:DefaultPassword` |
| `AllowedHosts` hostnames | SMTP password (and DB-stored `Smtp:PasswordEncrypted`) |
| Placeholder Production templates | Any vault tokens / FTP passwords |

---

## 5. Secrets Required

| Secret key | Expected by | Present in repo Production JSON? | Must supply on server? |
|------------|-------------|----------------------------------|------------------------|
| `Jwt:Key` | `SecretConfiguration` / JWT bearer | **No** | **Yes** |
| `Security:DataProtectionKey` | PII encrypt + SMTP password encrypt | **No** | **Yes** |
| `ConnectionStrings:DefaultConnection` (credentials) | EF + ADO pass numbers | Placeholder only | **Yes** |
| `Seed:DefaultPassword` | First-time seed users | **No** | **Yes** (first create only) |
| SMTP password | Settings UI → encrypted in DB | Not in Production JSON | Via UI after keys exist, or secure config |
| FTP / IIS / Windows / SQL admin passwords | Ops only | N/A | Ops; not app config keys |
| Facebook / OAuth / third-party API keys | — | **Not used** in codebase | N/A |

### Accidental secret commit scan (keys only — values not disclosed)

| Check | Result |
|-------|--------|
| `Jwt:Key` in `appsettings.Production.json` | **Absent** (good) |
| `Security:DataProtectionKey` in Production JSON | **Absent** (good) |
| Connection string password in Production JSON | Placeholder `SET_VIA_ENVIRONMENT_OR_SECRET_STORE` only |
| `secrets.json` tracked | **Not found** |
| Dev SMTP username in `appsettings.Development.json` | Non-password identity present (dev only; do not treat as prod secret store) |

**Secrets status:** **BLOCKED** until server-side secrets are generated and injected.

---

## 6. IIS Requirements

### IIS PRE-REQUISITES

1. Windows Server with IIS  
2. **.NET 9 ASP.NET Core Hosting Bundle** (ANCM V2)  
3. HTTPS certificate for `visitors.tiaanosoft.com`  
4. SQL Server reachable from the app pool identity / connection string  
5. App pool: **No Managed Code**, identity with rights to app folders  
6. URL Rewrite + Application Request Routing (recommended) if SPA and API are separate apps under one host  

### IIS CONFIGURATION REQUIRED

| Item | Requirement |
|------|-------------|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| Hosting model | In-process (from published `web.config`) |
| Site binding | HTTPS `visitors.tiaanosoft.com` |
| SPA | Serve `frontend/dist` as static site root (or equivalent) |
| `/api` | Reverse-proxy to API application (same host) |
| `/branding` | Proxy to API (API serves `wwwroot/branding`) **or** duplicate default branding on static site |
| SPA deep links | URL Rewrite → `index.html` for non-file routes; exclude `/api` and `/branding` |
| stdout logs | Optional: enable `stdoutLogEnabled` and create `logs\` writable |
| Do not delete FTP root samples | Leave `iisstart.htm` / `iisstart.png` / `index.htm.txt` unless admin explicitly replaces site content later |

**Published `web.config` (API):** AspNetCoreModuleV2 → `Tiaano.Vms.Api.dll`, in-process.

**IIS status:** **READY AFTER CONFIGURATION** · FTP root mapping to this site: **HUMAN INPUT REQUIRED**

---

## 7. SQL Server Requirements

| Item | Finding |
|------|---------|
| Engine | SQL Server (EF Core SqlServer provider) |
| Database name (template) | `TiaanoVms` |
| Connection | SQL auth or Windows auth; `TrustServerCertificate` used in templates |
| Compatibility | .NET 9 / EF Core 9.0.4 — use a supported modern SQL Server (2019/2022 typical) |
| App behavior | On start: `MigrateAsync()` applies pending EF migrations, then idempotent seed |
| Seed content | Roles; default tenant; system settings defaults; “Others” purpose; feedback question defaults; product modules/license entitlements; pass series; **superadmin** + **admin** users if missing and `Seed:DefaultPassword` set |
| Demo visitors | Not seeded as sample visitors in current seeder |

**Database status:** **READY AFTER CONFIGURATION** (create empty DB + credentials + first successful app start)

---

## 8. EF Migration Procedure

Latest relevant migrations include (among others):

- `20260918174905_AddVisitRowVersion` — `VisitorVisits.RowVersion`  
- `20260919073825_AddVisitFeedback` — feedback tables  
- Pass number, face descriptor, tenant/site isolation migrations earlier in chain  

### NEW production database

1. Create empty database + login/user.  
2. Configure connection string + secrets.  
3. Start the API once → **`MigrateAsync` applies all migrations** (preferred path matching application design).  
4. Optional equivalent: `dotnet ef database update` from a secure ops machine **only** against the new empty DB — not required if startup migrate succeeds.  
5. Confirm `__EFMigrationsHistory` ends at `20260919073825_AddVisitFeedback` (or latest at deploy time).

### EXISTING production database

1. **Do not** assume empty.  
2. Backup DB first.  
3. Compare `__EFMigrationsHistory` to the published migration set.  
4. Apply only pending migrations (startup migrate or controlled `ef database update`).  
5. Verify RowVersion column and feedback tables before enabling concurrent checkout / feedback features in ops.

**This audit did not run migrations against any server.**

**Migration status:** **READY AFTER CONFIGURATION** (procedure clear; execution is ops)

---

## 9. Media Storage Requirements

| Item | Finding |
|------|---------|
| Path | `{ContentRoot}/App_Data/media/tenants/{tenantId:N}/visitors/` |
| Public wwwroot | Visitor photos are **not** publicly static; served via **`GET /api/media/{fileName}`** with **`[Authorize]`** |
| Formats | JPEG / PNG / WebP; magic-byte check; **≤ 5 MB** |
| Face validation | Exactly one detectable face before permanent save |
| Writable | `App_Data` (media, mail-pickup if used), `wwwroot/branding` (logo uploads), `logs` if enabled |
| Must NOT be public | Entire `App_Data` tree |
| Face models | `MlModels/det_10g.onnx`, `MlModels/w600k_r50.onnx` (not in git; fetch via `scripts/fetch-face-models.ps1`; **not** in default publish unless copied) |
| Backup | Include tenant media directories in file backup |

**Permissions (recommended):** IIS app pool identity **Modify** on `App_Data`, `wwwroot/branding`, and `logs` (if used). **Read/execute** on app binaries and `MlModels`.

**Media status:** **READY AFTER CONFIGURATION** (create folders + ACLs; deploy models if face required)

---

## 10. Domain / HTTPS / CORS

| Item | Current repo Production template | Required for declared site |
|------|----------------------------------|----------------------------|
| Public URL | `vms.tiaano.local` (placeholder) | `https://visitors.tiaanosoft.com/` |
| API URL (from SPA) | Relative `/api` | `https://visitors.tiaanosoft.com/api` |
| Health URL | — | `https://visitors.tiaanosoft.com/api/system/health` |
| CORS | `https://vms.tiaano.local` | Must list `https://visitors.tiaanosoft.com` |
| AllowedHosts | `vms.tiaano.local;localhost` | Must allow `visitors.tiaanosoft.com` |
| HTTPS redirection | Enabled when not Development | Certificate + binding required |
| Cookies | JWT in **localStorage/sessionStorage** (not httpOnly cookies) | Mixed-content risk if page is HTTPS and API were HTTP — avoid by same-origin HTTPS |
| Tenant | From JWT claims; client `X-Tenant-Id` ignored for switching | No extra domain tenant header required |

**Domain status:** **READY AFTER CONFIGURATION** (CORS + AllowedHosts + cert + IIS bindings)

---

## 11. Health Checks

| Endpoint | Auth | Behavior |
|----------|------|----------|
| **`GET /api/system/health`** | Anonymous | Returns `database` bool via `CanConnectAsync`; `status` Healthy/Degraded; version; environment; UTC |
| `GET /api/system/version` | Authorized | Version metadata |
| ASP.NET `MapHealthChecks` | **Not registered** | Do not invent `/health` at root |

Exact production URL (given declared domain + same-origin API):

`https://visitors.tiaanosoft.com/api/system/health`

**Health status:** **READY** (endpoint exists; needs live IIS+SQL to return Healthy)

---

## 12. FileZilla Upload Structure

FTP root currently shows IIS default samples (`iisstart.htm`, `iisstart.png`, `index.htm.txt`).  
**Whether that FTP account is the final `visitors.tiaanosoft.com` site root:** **HUMAN/SERVER ADMIN VERIFICATION REQUIRED** — cannot be proven from the repository alone.

### Recommended logical layout (do not assume FTP = this path)

```
SERVER (example)
├── site-root/                    ← SPA (frontend/dist contents)
│   ├── index.html
│   ├── assets/
│   └── branding/ (optional static defaults)
└── api-app/                      ← deploy/backend contents
    ├── Tiaano.Vms.Api.dll
    ├── web.config
    ├── appsettings.json
    ├── appsettings.Production.json  ← SERVER-CONFIGURED secrets
    ├── wwwroot/branding/
    ├── App_Data/media/              ← SERVER-GENERATED / writable
    ├── MlModels/                    ← SERVER-CONFIGURED (copy models)
    └── logs/                        ← SERVER-GENERATED (optional)
```

IIS must wire SPA + `/api` proxy as in §6.

### UPLOAD (when deploy is approved later)

| Upload | Source |
|--------|--------|
| SPA files | `frontend/dist/**` |
| API files | `deploy/backend/**` (binaries, deps, `web.config`, non-secret appsettings) |
| Branding defaults | Included under publish `wwwroot/branding` |
| Face models | Copy ONNX into `MlModels` on server (not from git) |

### DO NOT UPLOAD

| Item | Reason |
|------|--------|
| Source trees (`frontend/src`, `backend/*.cs`) | Not needed for runtime |
| `node_modules`, `bin/obj`, test projects | Dev only |
| Local `App_Data/media` / mail-pickup from workstation | Wrong tenant data / PII risk |
| User secrets / `.env` / vault exports into git paths | Secret leakage |
| Overwrite live `iisstart.*` unless replacing site intentionally | Admin instruction: leave alone for now |

### SERVER-GENERATED

- `App_Data/media/tenants/...`  
- Optional `App_Data/mail-pickup`  
- `logs/stdout*` if enabled  
- EF schema via migrate on start  

### SERVER-CONFIGURED

- Production connection string, JWT key, DataProtectionKey, Seed password (first run)  
- CORS / AllowedHosts for `visitors.tiaanosoft.com`  
- HTTPS certificate  
- App pool identity ACLs  

**FileZilla status:** **HUMAN INPUT REQUIRED** (confirm site root vs FTP home before any upload)

---

## 13. Files That Must NOT Be Uploaded

- Repository `.git` history  
- `backend.Tests`, agent transcripts, local OneDrive junk  
- Workstation visitor photos under `backend/App_Data/media`  
- Any file containing real `Jwt:Key` / encryption keys into a shared insecure drop folder without encryption  
- `appsettings.Development.json` as the Production environment file (wrong SMTP SSL behavior risk)  

---

## 14. Backup Before Deployment

Before first production cutover (ops performs):

1. **If replacing an existing site:** backup current IIS site folder + `web.config`  
2. **SQL:** full backup of target database (if exists)  
3. **Media:** existing `App_Data/media` if any prior VMS data  
4. **Secrets:** export/backup of planned Production secrets store (vault / secure note) — not into FTP  
5. **Certificates:** confirm export/renewal process  
6. **FTP/IIS default files:** leave `iisstart.*` intact until cutover plan says otherwise  

---

## 15. Rollback Procedure

**Minimal rollback (after a failed cutover):**

1. Stop / recycle new app pool.  
2. Restore previous site files from backup (or re-enable prior IIS physical path).  
3. Restore SQL database from pre-migration backup if migrations were applied.  
4. Restore `App_Data/media` if overwritten.  
5. Restore previous secrets/config if keys changed (critical if DataProtectionKey rotated).  
6. Verify `https://visitors.tiaanosoft.com/` and `/api/system/health`.  

Do **not** rotate `Security:DataProtectionKey` between backup and rollback without accepting loss of decrypt for ID numbers / SMTP password ciphertext.

---

## 16. Post-Deployment Smoke Test

1. Website loads at `https://visitors.tiaanosoft.com/`.  
2. HTTPS valid (no cert warning in target browsers).  
3. Login works (`superadmin`/`admin` after seed password set; then force change if required).  
4. JWT: authenticated API calls succeed; logout clears tokens.  
5. Tenant resolution: authenticated calls work; forged tenant header does not switch tenant.  
6. Dashboard loads for admin.  
7. Visitor registration works.  
8. Exactly one face accepted on capture/validate.  
9. Two or more faces rejected (`MULTIPLE_FACES_DETECTED` / standard message).  
10. Approval path if enabled in settings.  
11. Check-in works.  
12. Visitor pass print/PDF works.  
13. Currently Inside works.  
14. Check-out works (submit feedback first if active questions exist).  
15. Feedback questions/responses work.  
16. Reports export works.  
17. Visitor media: photo URL requires auth; direct disk path not publicly browsable.  
18. SQL connectivity: health shows `database: true`.  
19. Health: `GET /api/system/health` → Healthy.  
20. No dev-only exposure: Swagger off; localhost CORS not relied upon; no workstation media.

---

## 17. Known Risks

| Risk | Severity | Notes |
|------|----------|-------|
| Production CORS/AllowedHosts still placeholder `vms.tiaano.local` | **High** | App will reject host / block browser CORS until updated |
| Missing JWT / DataProtection keys | **High** | Process will not start |
| SPA not served by API | **High** | Mis-copying dist into API wwwroot alone will not work with current `Program.cs` |
| Face models absent | **Medium** | Face features degrade / photo gate may fail if detector required |
| MailKit NU1902 | **Low/Med** | Does not block publish; track separately |
| VisitConcurrency tests vs feedback gate | **Low** | Dev test-DB issue; ensure smoke test submits feedback before checkout when questions active |
| FTP root ≠ final site | **High** until verified | Uploading blind may hit wrong directory |
| DataProtectionKey rotation | **High** | Breaks decrypt of stored PII/SMTP secrets |
| SkiaSharp native assets | **Low** | Win32 package present for Windows; confirm on target OS |

---

## 18. Human Inputs Required

1. Confirm FTP home directory ↔ IIS site for `visitors.tiaanosoft.com`.  
2. Decide IIS layout: static SPA + `/api` reverse proxy (recommended) vs other admin-approved design.  
3. Generate and store: SQL connection, `Jwt:Key`, `Security:DataProtectionKey`, first-run `Seed:DefaultPassword`.  
4. Set Production `Cors:Origins` and `AllowedHosts` to `https://visitors.tiaanosoft.com` / `visitors.tiaanosoft.com`.  
5. Create SQL database and login; network allowlist from web server.  
6. Install .NET 9 Hosting Bundle; bind HTTPS certificate.  
7. Decide whether face recognition is required at go-live; if yes, deploy ONNX models to `MlModels`.  
8. Plan SMTP (Settings UI after boot vs config); ensure `IgnoreSslErrors` stays false.  
9. Confirm backup owners and rollback window.  
10. Do **not** delete `iisstart.htm` / `iisstart.png` / `index.htm.txt` until cutover explicitly replaces the site.

---

## 19. Final Deployment Readiness

| Area | Status |
|------|--------|
| Frontend build package | **READY** |
| Backend publish package | **READY** |
| ImageSharp / license gate | **READY** |
| Configuration templates | **READY AFTER CONFIGURATION** |
| Secrets | **BLOCKED** (must supply on server) |
| IIS | **READY AFTER CONFIGURATION** |
| Database | **READY AFTER CONFIGURATION** |
| Media | **READY AFTER CONFIGURATION** |
| Domain / CORS / HTTPS | **READY AFTER CONFIGURATION** |
| FileZilla / FTP mapping | **HUMAN INPUT REQUIRED** |
| Overall go-live | **READY AFTER CONFIGURATION** |

**Not BLOCKED by code/build.**  
**Blocked from immediate upload/go-live only by missing ops configuration, secrets, and FTP/site verification.**

---

*Related docs: `docs/DEPLOYMENT_WINDOWS_SERVER.md` (partially outdated on SPA-in-wwwroot and uploads path), `docs/IMAGESHARP_LICENSE_USAGE_AUDIT.md`, `docs/FACE_PIPELINE_REVIEW.md`.*
