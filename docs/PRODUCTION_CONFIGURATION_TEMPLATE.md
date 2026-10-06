# TIAANO VMS Production Configuration Template

**Purpose:** Safe, placeholder-only production configuration guide.  
**Does not contain secrets.** Do not paste real passwords, keys, or connection strings into this file or into git.

**Declared production site:** `https://visitors.tiaanosoft.com/`  
**Related audit:** `docs/PRODUCTION_DEPLOYMENT_READINESS.md`

---

## 1. Exact production URLs (from source)

| Role | Exact URL |
|------|-----------|
| Frontend (SPA) | `https://visitors.tiaanosoft.com/` |
| API (axios `baseURL`) | **`/api`** (relative) → resolves to **`https://visitors.tiaanosoft.com/api`** |
| Health check | `https://visitors.tiaanosoft.com/api/system/health` |
| Branding (API static) | `https://visitors.tiaanosoft.com/branding/...` |
| Private visitor media | `https://visitors.tiaanosoft.com/api/media/{fileName}` (requires JWT) |

### How the frontend chooses the API

| Source | Behavior |
|--------|----------|
| `frontend/src/lib/api.ts` | `axios.create({ baseURL: '/api' })` — **hardcoded relative path** |
| Refresh calls | Same origin: `'/api/auth/refresh'` |
| `VITE_*` / `.env` | **No** committed `.env`; API base is **not** driven by `VITE_API_URL` |
| `VITE_API_ORIGIN` | Optional in `assetUrl()` only (absolute prefix for assets). Not required when SPA and API share origin |
| `vite.config.ts` | Dev-only proxy: `/api` → `http://127.0.0.1:5080` — **not** used in production `dist` |
| Production `dist` | Expects **same host** so `/api` hits the API via IIS reverse proxy |

**There is no hardcoded production hostname in the React client.**  
**Intended production API URL:** `https://visitors.tiaanosoft.com/api`

---

## 2. Exact CORS / AllowedHosts values

Committed `backend/appsettings.Production.json` still uses placeholder host `vms.tiaano.local`.  
For the declared website, production **must** use:

| Setting | Exact production value |
|---------|------------------------|
| `Cors:Origins` | **`https://visitors.tiaanosoft.com`** (array element; include scheme, no trailing path) |
| `AllowedHosts` | **`visitors.tiaanosoft.com`** (hostname only; semicolon-separated if multiple) |

Optional additions (only if you truly serve those hosts):

- `Cors:Origins`: also `https://www.visitors.tiaanosoft.com` **if** that hostname is used  
- `AllowedHosts`: same hostnames without scheme  

Do **not** leave `vms.tiaano.local` in Production once live on `visitors.tiaanosoft.com`.

---

## 3. Safe vs secret configuration

### SAFE TO COMMIT / bake into server `appsettings.Production.json` (non-secret)

```json
{
  "Jwt": {
    "Issuer": "Tiaano.Vms",
    "Audience": "Tiaano.Vms.Clients",
    "AccessTokenMinutes": 30
  },
  "Cors": {
    "Origins": [ "https://visitors.tiaanosoft.com" ]
  },
  "AllowedHosts": "visitors.tiaanosoft.com",
  "Smtp": {
    "Enabled": true,
    "Host": "<SMTP_HOST>",
    "Port": 587,
    "EnableSsl": true,
    "Username": "<SMTP_USERNAME>",
    "FromAddress": "<FROM_ADDRESS>",
    "FromName": "Visitor Management",
    "IgnoreSslErrors": false,
    "TimeoutSeconds": 45
  },
  "FaceRecognition": {
    "Enabled": true,
    "ModelDirectory": "MlModels",
    "DetectionSize": 640,
    "DetectionScoreThreshold": 0.5,
    "MinimumFacePixels": 60,
    "MatchThreshold": 0.42
  },
  "Reminders": {
    "PollMinutes": 5
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

SMTP host/username/from are operational identifiers; **SMTP password must not** be committed in clear text (prefer Settings UI after boot → stored encrypted in DB).

### MUST BE SERVER ENVIRONMENT VARIABLES / SECRET STORE (never commit)

Use ASP.NET Core environment variable names (`__` = nested `:`):

```text
ASPNETCORE_ENVIRONMENT=Production

ConnectionStrings__DefaultConnection=<PRODUCTION_SQL_CONNECTION>

Jwt__Key=<GENERATE_SECURE_RANDOM_SECRET_AT_LEAST_32_BYTES>

Security__DataProtectionKey=<GENERATE_SECURE_KEY_AT_LEAST_16_CHARS_PREFER_32_PLUS>

Seed__DefaultPassword=<ONE_TIME_FIRST_RUN_PASSWORD>
```

Optional (if not using Settings UI for mail password):

```text
Smtp__Password=<SMTP_PASSWORD>
```

(Application primarily persists SMTP password encrypted via Settings after `Security:DataProtectionKey` exists.)

---

## 4. Full environment-variable template (placeholders only)

```text
# --- Required ---
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=Server=<SQL_HOST>;Database=TiaanoVms;User Id=<SQL_APP_USER>;Password=<SQL_APP_PASSWORD>;TrustServerCertificate=True;MultipleActiveResultSets=true;Connect Timeout=15;Pooling=true;Max Pool Size=100
Jwt__Key=<GENERATE_SECURE_RANDOM_SECRET>
Security__DataProtectionKey=<GENERATE_SECURE_KEY>
Seed__DefaultPassword=<ONE_TIME_FIRST_RUN_PASSWORD>

# --- Strongly recommended non-secret (or set in appsettings.Production.json on server) ---
Cors__Origins__0=https://visitors.tiaanosoft.com
AllowedHosts=visitors.tiaanosoft.com
Jwt__Issuer=Tiaano.Vms
Jwt__Audience=Tiaano.Vms.Clients

# --- Optional ---
FaceRecognition__Enabled=true
FaceRecognition__ModelDirectory=MlModels
Smtp__Enabled=true
Smtp__Host=<SMTP_HOST>
Smtp__Port=587
Smtp__EnableSsl=true
Smtp__IgnoreSslErrors=false
```

**Rules enforced by code:**

- `Jwt:Key` required; ≥ 32 UTF-8 bytes; Production rejects placeholder-like values (`YOUR_`, `CHANGE_ME`, `REPLACE`, …).  
- `Security:DataProtectionKey` required; ≥ 16 UTF-8 bytes; same placeholder rejection in Production.  
- `Smtp:IgnoreSslErrors` **must be false** in Production (startup fails if true).

---

## 5. Secrets catalog (no values)

| Configuration key | Purpose | Read by | Recommended storage |
|-------------------|---------|---------|---------------------|
| `ConnectionStrings:DefaultConnection` | SQL EF + ADO | `Program.cs`, `PassNumberService` | Env / vault / IIS env |
| `Jwt:Key` | Sign/validate access tokens | `SecretConfiguration`, JWT bearer, `AuthService` | Env / vault |
| `Security:DataProtectionKey` | AES key material for ID numbers + SMTP password ciphertext (`SensitiveDataHelper`) | `SecretConfiguration`, Visitor/Settings services | Env / vault; **persist forever** for that DB |
| `Seed:DefaultPassword` | Create `superadmin` / `admin` on first boot only | `DbSeeder` via `GetSeedPasswordForCreateOnly` | Env once; remove after first login + password change |
| SMTP password | Outbound mail | Settings → `Smtp:PasswordEncrypted` in DB (preferred) | Settings UI after deploy; not git |
| FTP / Windows / SQL sa | Ops access | Outside app | Ops vault only |

**Not used:** Facebook/OAuth/third-party API keys (none in codebase).

---

## 6. Data Protection (important naming note)

`Security:DataProtectionKey` is **not** the ASP.NET Core Data Protection key-ring API (`AddDataProtection` / XML key files).

It is a **custom application secret** hashed with SHA-256 to derive an AES key for:

- Visitor ID number encryption at rest  
- SMTP password encryption in `SystemSettings`

| If key is lost / replaced | Effect |
|---------------------------|--------|
| New random key | Existing ID ciphertext and SMTP password ciphertext **cannot be decrypted** |
| App still starts | Yes, if new key meets length rules |
| Recovery | Restore the **exact previous key**, or re-enter IDs / reconfigure SMTP |

**Production requirement:** store this value in a durable secret store and back it up with the same care as the database. Do not rotate casually.

ASP.NET Core’s own Data Protection (cookies/antiforgery) is not the primary auth mechanism here (JWT in browser storage). No separate “persist keys to disk” configuration was found for that path in `Program.cs`.

---

## 7. IIS routing requirements

Frontend calls **relative** `/api`. API does **not** serve the React SPA (`Program.cs` only exposes `/branding` static files + `/api/*` controllers).

| Public path | Target |
|-------------|--------|
| `/` and SPA routes | Static files from `frontend/dist` (`index.html` + `assets/`) |
| `/api/*` | ASP.NET Core app (`deploy/backend`, ANCM in-process) |
| `/branding/*` | API `wwwroot/branding` (proxy to API) **or** static defaults on SPA site |

**Required IIS features:** URL Rewrite (SPA fallback) + **ARR reverse proxy** (or equivalent) so `https://visitors.tiaanosoft.com/api` reaches the API app while the browser origin stays `https://visitors.tiaanosoft.com`.

SPA fallback rule must **exclude** `/api` and `/branding`.

Published API `web.config`: AspNetCoreModuleV2, `hostingModel="inprocess"`, entry `Tiaano.Vms.Api.dll`.  
Set `ASPNETCORE_ENVIRONMENT=Production` on the API app pool / site.

---

## 8. SQL Server requirements

| Item | Requirement |
|------|-------------|
| Compatibility | Modern SQL Server supporting EF Core 9 / `rowversion` (2019/2022 typical) |
| Database name (template) | `TiaanoVms` |
| Login/user | Dedicated app login (not `sa`); rights to create/alter schema for first migrate, then least privilege if hardened later |
| Connection string format | See env template above (`User Id`/`Password` or Windows auth equivalent) |
| Migrations | On API start: `DbSeeder` → `Database.MigrateAsync()` applies pending EF migrations |
| First-run seed | Roles, default tenant, settings defaults, pass series, entitlements; users `superadmin`/`admin` if `Seed:DefaultPassword` set |
| Backup | Full backup **before** first migrate on any existing DB; nightly backups in production |

Do not run migrations from this documentation task.

---

## 9. Media directories

| Path | Purpose | Public? | Writable by app pool? |
|------|---------|---------|------------------------|
| `{ContentRoot}/App_Data/media/tenants/{tenantId:N}/visitors/` | Visitor photos | **No** — only via `/api/media` + JWT | **Yes (Modify)** |
| `{ContentRoot}/App_Data/mail-pickup/` | Optional SMTP pickup | No | Yes if used |
| `{ContentRoot}/wwwroot/branding/` | Logos | Yes under `/branding` | Yes (logo upload) |
| `{ContentRoot}/logs/` | Optional stdout | No | Yes if logging enabled |

App creates media/branding roots on start when possible. Ensure ACLs before go-live. **Backup** tenant media with SQL backups.

Limits (code): JPEG/PNG/WebP; ≤ 5 MB; exactly one face when detector available.

---

## 10. ONNX / face models

| Item | Detail |
|------|--------|
| Directory | `{ContentRoot}/MlModels` (config: `FaceRecognition:ModelDirectory`) |
| Required files | **`det_10g.onnx`**, **`w600k_r50.onnx`** |
| In Git? | **No** (`backend/MlModels/*` gitignored except `.gitkeep`) |
| Fetch (build machine) | `scripts/fetch-face-models.ps1` |
| Copy to server | Separate ops step into API content root `MlModels\` |
| If missing | App **starts**; `IsDetectionAvailable` / `IsAvailable` false; face enrol/search/checkout-by-face unavailable; photo gate returns detector-unavailable when detection required |

---

## 11. Production checklist

### PRE-UPLOAD

- [ ] FTP root confirmed as IIS site for `https://visitors.tiaanosoft.com/`
- [ ] IIS site confirmed (SPA + `/api` proxy plan)
- [ ] SQL Server ready (instance, DB, login)
- [ ] Database backup ready (if DB exists)
- [ ] .NET 9 Hosting Bundle installed
- [ ] HTTPS certificate ready for `visitors.tiaanosoft.com`
- [ ] Production secrets ready (SQL, Jwt:Key, DataProtectionKey, Seed password) — **not in git**
- [ ] CORS configured: `https://visitors.tiaanosoft.com`
- [ ] AllowedHosts configured: `visitors.tiaanosoft.com`
- [ ] Frontend API URL verified: same-origin `/api` → `https://visitors.tiaanosoft.com/api`
- [ ] MlModels available on server (if face required at go-live)
- [ ] Media directories / ACLs ready (`App_Data`)
- [ ] Do not delete `iisstart.htm` / `iisstart.png` / `index.htm.txt` until cutover replaces site

### POST-UPLOAD

- [ ] IIS / app pool starts (`ASPNETCORE_ENVIRONMENT=Production`)
- [ ] API health works: `/api/system/health`
- [ ] Database health works (`database: true`)
- [ ] Login works; change seed passwords
- [ ] Visitor registration works
- [ ] Single face accepted
- [ ] Multiple faces rejected
- [ ] Check-in works
- [ ] Pass works
- [ ] Checkout works (submit feedback first if questions active)
- [ ] Media works privately (auth required)
- [ ] Reports work
- [ ] Tenant isolation verified

---

## 12. Exact remaining human actions

1. Confirm FTP ↔ IIS mapping for `visitors.tiaanosoft.com`.  
2. Install Hosting Bundle; bind HTTPS; create SPA site + API app + `/api` (and `/branding`) reverse proxy.  
3. Create SQL database/login; prepare connection string **offline**.  
4. Generate and store `Jwt:Key`, `Security:DataProtectionKey`, one-time `Seed:DefaultPassword` in a secret store.  
5. Apply Production CORS + AllowedHosts for `visitors.tiaanosoft.com` on the server (env or server-local JSON — **not this repo file with real secrets**).  
6. Copy `frontend/dist` and `deploy/backend`; copy ONNX models if needed; set folder ACLs.  
7. Start API once (migrations + seed); run smoke tests; remove/rotate seed password after admin password change.  
8. Configure SMTP via Settings UI (or secure config) with `IgnoreSslErrors=false`.

**Do not deploy from this task. Do not commit secrets. Do not push.**
