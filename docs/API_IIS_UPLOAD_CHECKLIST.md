# API IIS Upload Checklist — `/api` on visitors.tiaanosoft.com

**Frontend live:** `https://visitors.tiaanosoft.com/`  
**API target:** `https://visitors.tiaanosoft.com/api` (FTP empty `/api` folder → IIS Application)  
**Publish folder (local):** `deploy/backend`  
**Do not commit secrets. Do not invent passwords in git.**

---

## Architecture note (required code prep completed)

Controllers no longer use a duplicate `api/` route segment. The app uses **`ASPNETCORE_PATHBASE=/api`** so:

| Browser URL | PathBase | Routed path |
|-------------|----------|-------------|
| `/api/system/health` | `/api` | `/system/health` |
| `/api/auth/login` | `/api` | `/auth/login` |
| `/api/media/{file}` | `/api` | `/media/{file}` (JWT) |
| `/api/branding/...` | `/api` | `/branding/...` (static logos) |

Frontend `axios` `baseURL: '/api'` is unchanged.

**IIS:** Convert the site’s `/api` folder to an **Application** (own app pool, No Managed Code) pointing at the uploaded publish files.

Optional parent-site rewrite: `/branding/*` → `/api/branding/*` if custom logos stored by the API must load at `/branding/...` URLs (SPA already ships default branding under the site root).

---

## 1. Inspected publish output (`deploy/backend`)

| Item | Status |
|------|--------|
| `Tiaano.Vms.Api.dll` / `.exe` | Present |
| `web.config` (ANCM in-process, .NET 9) | Present — `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_PATHBASE=/api` |
| `appsettings.json` / `.Production.json` | Present — **no Jwt:Key / DataProtectionKey** |
| `wwwroot/branding` | Present (logos) |
| `MlModels/*.onnx` | **Not in publish** — copy separately |
| `App_Data` | Created at runtime on server |

**Static exposure:** ANCM `path="*"` sends HTTP to Kestrel. App only serves static files from `wwwroot/branding` at `/branding` (→ public `/api/branding`). **`appsettings*.json`, DLLs, and `App_Data` are not mapped as static content.** `App_Data` is also an IIS hidden segment.

---

## 2. Files to UPLOAD to FTP `/api`

Upload **contents** of `deploy/backend` into the site’s `/api` directory (so `web.config` sits at `/api/web.config`):

- All `*.dll`, `Tiaano.Vms.Api.exe`, `Tiaano.Vms.Api.deps.json`, `runtimeconfig.json`
- `web.config`
- `appsettings.json`, `appsettings.Production.json` (placeholders only; secrets via env)
- `runtimes/` (native deps, incl. ONNX/Skia as applicable)
- `wwwroot/branding/` (and locale/font folders if present)
- Do **not** rely on uploading `appsettings.Development.json` for Production (optional to omit)

**Also copy (not in default publish):**

```
/api/MlModels/det_10g.onnx
/api/MlModels/w600k_r50.onnx
```

(from a machine that ran `scripts/fetch-face-models.ps1`)

---

## 3. Files that must NOT be uploaded

| Do not upload | Why |
|---------------|-----|
| Source (`backend/*.cs`, `frontend/src`) | Not needed |
| `backend.Tests`, `.git` | Dev only |
| Local `App_Data/media` from workstation | Wrong/PII data |
| Real secrets files / `.env` with production keys | Leakage |
| Overwrite site-root `iisstart.*` | Leave SPA root alone |
| Commit/push of production passwords | Policy |

---

## 4. IIS requirements

1. **.NET 9 ASP.NET Core Hosting Bundle** installed; `iisreset` after install  
2. Site root = React SPA (already working)  
3. `/api` = **IIS Application**  
   - App pool: **No Managed Code**, Integrated, 64-bit  
   - Identity: AppPoolIdentity or dedicated account  
4. HTTPS binding for `visitors.tiaanosoft.com` (already on site)  
5. URL Rewrite (optional): SPA fallback on **parent** site only; exclude `/api`  
6. Optional: rewrite `/branding` → `/api/branding` for API-uploaded logos  

---

## 5. Required server environment variables / settings

Set on the **API app pool** or IIS Configuration Editor for the `/api` application (not in git):

| Name | Required | Notes |
|------|----------|--------|
| `ASPNETCORE_ENVIRONMENT` | Yes | `Production` (also in `web.config`) |
| `ASPNETCORE_PATHBASE` | Yes | `/api` (also in `web.config`) |
| `ConnectionStrings__DefaultConnection` | Yes | Real SQL connection |
| `Jwt__Key` | Yes | ≥32 bytes entropy |
| `Security__DataProtectionKey` | Yes | Persist forever for that DB |
| `Seed__DefaultPassword` | First run only | Create `superadmin`/`admin`; remove after |
| `Cors__Origins__0` | Yes if not using updated Production JSON | `https://visitors.tiaanosoft.com` |
| `AllowedHosts` | Yes if overriding JSON | `visitors.tiaanosoft.com` |

Published `appsettings.Production.json` already sets CORS origin and AllowedHosts for `visitors.tiaanosoft.com` (non-secret). Connection string in that file remains a **placeholder** — override with env.

---

## 6. Database requirements

- SQL Server with database (e.g. `TiaanoVms`) and app login  
- Backup before first start if DB already exists  
- On first successful API start: `MigrateAsync()` applies EF migrations + seed  
- Do not run migrations from a laptop against production unless ops-approved  

---

## 7. Permissions (API app pool identity)

| Path | Rights |
|------|--------|
| `/api` (app files) | Read & execute |
| `/api/App_Data` (create if needed) | **Modify** (media, mail-pickup) |
| `/api/wwwroot/branding` | **Modify** (logo upload) |
| `/api/logs` (if stdout enabled) | **Modify** |
| `/api/MlModels` | Read |

Media stays under `App_Data/media/tenants/{tenantId}/visitors/` — private; only `/api/media/...` with JWT.

---

## 8. Post-deployment smoke tests

1. `GET https://visitors.tiaanosoft.com/api/system/health` → JSON, `database: true` when SQL OK  
2. Confirm `https://visitors.tiaanosoft.com/api/appsettings.json` is **not** downloadable (404/HTML error, not file contents)  
3. Login from SPA  
4. Register visitor; single-face accept; multi-face reject  
5. Check-in / pass / checkout (feedback if questions active)  
6. Photo loads via authenticated `/api/media/...` only  
7. Reports  
8. Face search/checkout if models deployed  

---

## 9. Exact next deployment steps (human)

1. On SQL Server: create DB + login; note connection string offline.  
2. Generate `Jwt__Key`, `Security__DataProtectionKey`, one-time `Seed__DefaultPassword`; store in vault.  
3. In IIS: ensure `/api` is an **Application** with .NET 9-capable pool.  
4. Set environment variables above on that application/pool.  
5. FTP upload `deploy/backend/**` into `/api` (overwrite publish files only).  
6. Upload `MlModels` ONNX files if face required.  
7. Grant Modify on `App_Data` / branding / logs.  
8. Recycle app pool.  
9. Hit `/api/system/health`; then SPA login smoke tests.  
10. Change seed user passwords; clear `Seed__DefaultPassword` from env.  

**MailKit NU1902** remains a warning only — does not block publish.
