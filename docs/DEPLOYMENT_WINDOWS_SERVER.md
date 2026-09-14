# Windows Server / IIS Deployment

## Required components

1. Windows Server 2019/2022
2. IIS with ASP.NET Core Module
3. [.NET 9 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/9.0)
4. SQL Server on same network/intranet
5. TLS certificate (internal CA or public)

## 1. SQL Server

```sql
CREATE DATABASE TiaanoVms;
CREATE LOGIN vms_app WITH PASSWORD = 'StrongPasswordHere!';
USE TiaanoVms;
CREATE USER vms_app FOR LOGIN vms_app;
ALTER ROLE db_owner ADD MEMBER vms_app; -- or grant least-privilege schema rights
```

## 2. Application files

On the build machine:

```powershell
cd frontend
npm ci
npm run build

cd ..\backend
dotnet publish -c Release -o C:\publish\tiaano-vms
```

Copy `frontend\dist\*` into `C:\publish\tiaano-vms\wwwroot\`.

Copy branding:

```powershell
Copy-Item backend\wwwroot\branding\* C:\publish\tiaano-vms\wwwroot\branding\ -Recurse -Force
```

## 3. Production configuration

Create `C:\publish\tiaano-vms\appsettings.Production.json` **outside source control** (or use environment variables / Azure Key Vault equivalent on-prem):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=SQLHOST;Database=TiaanoVms;User Id=vms_app;Password=***;TrustServerCertificate=True;MultipleActiveResultSets=true"
  },
  "Jwt": {
    "Key": "LONG_RANDOM_SECRET_AT_LEAST_32_CHARS",
    "Issuer": "Tiaano.Vms",
    "Audience": "Tiaano.Vms.Clients"
  },
  "Security": {
    "DataProtectionKey": "ANOTHER_LONG_RANDOM_SECRET"
  },
  "Cors": {
    "Origins": [ "https://vms.tiaano.local" ]
  }
}
```

Set environment for the app pool / web.config:

```xml
<environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
```

## 4. IIS website

1. Create folder `C:\apps\tiaano-vms` and copy publish output.
2. Create Application Pool:
   - .NET CLR version: **No Managed Code**
   - Identity: ApplicationPoolIdentity (or dedicated service account)
3. Create Website:
   - Binding: `https://vms.tiaano.local:443`
   - Physical path: `C:\apps\tiaano-vms`
   - Application pool: the pool above
4. Ensure ASP.NET Core Hosting Bundle is installed; restart IIS:

```powershell
iisreset
```

## 5. Folder permissions

Grant IIS App Pool identity modify rights on:

- `C:\apps\tiaano-vms\wwwroot\uploads` (visitor photos)
- `C:\apps\tiaano-vms\logs` (if file logging enabled)

```powershell
New-Item -ItemType Directory -Force -Path C:\apps\tiaano-vms\wwwroot\uploads
icacls C:\apps\tiaano-vms\wwwroot\uploads /grant "IIS AppPool\TiaanoVms:(OI)(CI)M"
```

## 6. HTTPS certificate

1. Import certificate into Local Computer \ Personal
2. In IIS Bindings, select the certificate for HTTPS
3. Redirect HTTP → HTTPS (URL Rewrite or `UseHttpsRedirection`)

## 7. Static files / SPA

Serving the Vite build from API `wwwroot` is simplest for intranet. Ensure:

- `index.html` fallback for client routes (optional IIS URL Rewrite rule)
- `/uploads` and `/branding` remain accessible

Example rewrite for React Router:

```xml
<rule name="SPA">
  <match url=".*" />
  <conditions logicalGrouping="MatchAll">
    <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
    <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
    <add input="{REQUEST_URI}" pattern="^/(api|uploads|branding|swagger)" negate="true" />
  </conditions>
  <action type="Rewrite" url="/index.html" />
</rule>
```

## 8. Logging

- Application logs: stdout via `web.config` `aspNetCore` `stdoutLogEnabled="true"` to `.\logs\`
- IIS logs: `%SystemDrive%\inetpub\logs\LogFiles`
- SQL backups: nightly full + hourly log (Full recovery model recommended)

## 9. Backup strategy

| Asset | Frequency | Notes |
|-------|-----------|-------|
| SQL `TiaanoVms` | Nightly full | Retain 30 days |
| Transaction logs | Hourly | Point-in-time restore |
| `wwwroot\uploads` | Nightly file backup | Photos |
| `appsettings.Production.json` | Secure vault | Secrets |

## 10. Post-deploy checklist

- [ ] Browse https://vms.tiaano.local
- [ ] Login as reception
- [ ] Register visitor, approve, check-in, print pass
- [ ] Verify visitor by Visit Number on security tablet
- [ ] Confirm photos write to uploads
- [ ] Force password change for seed users
- [ ] Disable Swagger in Production (already gated to Development)
