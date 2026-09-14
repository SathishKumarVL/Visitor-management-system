# Installation Guide

## Prerequisites

- Windows 10/11 or Windows Server 2019+
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20 LTS+](https://nodejs.org/)
- Microsoft SQL Server 2019+ (Express/Standard/Enterprise)
- (Optional) SQL Server Management Studio

## Local development setup

### 1. Clone / open the project

```powershell
cd "c:\path\to\visitor management"
```

### 2. Create the database

```powershell
sqlcmd -S localhost -E -Q "IF DB_ID('TiaanoVms') IS NULL CREATE DATABASE TiaanoVms;"
```

### 3. Configure connection string

Edit `backend/appsettings.Development.json` or `backend/appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=TiaanoVms;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

Do **not** commit production secrets. Use User Secrets or environment variables:

```powershell
cd backend
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key" "your-long-random-key"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "your-connection-string"
```

### 4. Run migrations / seed

Startup runs `MigrateAsync` and seeds departments, purposes, locations, gates, settings, and sample users.

Manual migration:

```powershell
cd backend
dotnet ef database update
```

### 5. Start API

```powershell
cd backend
dotnet run --launch-profile http
```

### 6. Start UI

```powershell
cd frontend
npm install
npm run dev
```

Open http://localhost:5173

### 7. Verify

1. Login as `reception` using the password you configured in `Seed:DefaultPassword` (User Secrets)
2. Open Reception Mode → New Visitor
3. Complete wizard and submit
4. Login as `host` and approve
5. Check in and print pass

## Build for production

```powershell
cd frontend
npm run build

cd ..\backend
dotnet publish -c Release -o .\publish
```

Copy `frontend/dist` contents into `backend/publish/wwwroot` (or host separately in IIS). See `DEPLOYMENT_WINDOWS_SERVER.md`.
