# TIAANO Visitor Management System

Production-ready Visitor Management System for **TIAANO**, designed for reception/security tablets and Windows Server / IIS / SQL Server deployments.

## Stack

| Layer | Technology |
|-------|------------|
| Frontend | React, TypeScript, Vite, Tailwind CSS |
| Backend | ASP.NET Core Web API (.NET 9), C# |
| Data | Entity Framework Core + Microsoft SQL Server |
| Auth | ASP.NET Core Identity + JWT + role-based authorization |

## Roles

- Super Admin
- Admin
- Reception
- Security
- Host

## Quick start (local)

### Prerequisites

- .NET 9 SDK
- Node.js 20+
- SQL Server (local instance running)

### 1. Database

```powershell
sqlcmd -S localhost -E -Q "IF DB_ID('TiaanoVms') IS NULL CREATE DATABASE TiaanoVms;"
```

Connection string is in `backend/appsettings.json` (Windows auth by default).

### 2. Backend

```powershell
cd backend
dotnet restore
dotnet ef database update
dotnet run --launch-profile http
```

API: http://localhost:5080  
Swagger (Development): http://localhost:5080/swagger

Migrations apply automatically on startup via the seeder.

### 3. Frontend

```powershell
cd frontend
npm install
npm run dev
```

UI: http://localhost:5173

Vite proxies `/api`, `/uploads`, and `/branding` to the API.

### Seed users

On first run (when users do not already exist), seed accounts are created for
`superadmin` and `admin` only. Desk roles (`reception`, `security`, `host`) and all master data
(departments, hosts, purposes, locations, gates) are created by an administrator under Admin —
nothing fictional is pre-loaded.

Initial passwords come from `Seed:DefaultPassword` via User Secrets / environment variables — never from tracked config or docs. Seeded users are marked `MustChangePassword`. Existing users are never password-reset on startup. See [docs/SECURITY.md](docs/SECURITY.md).

To wipe leftover test or sample data from a local database:

```powershell
./scripts/purge-mock-data.ps1 -Force
```

Integration tests use a separate database (`TiaanoVms_Tests`) so they no longer write into the working database.

## Project layout

```
/backend     ASP.NET Core API
/frontend    React + Vite app
/database    SQL scripts / notes
/docs        Installation, deployment, API, security guides
/uploads     Runtime photo storage (wwwroot/uploads)
```

## Core workflows

1. Reception registers visitor (touch wizard)
2. Host approves/rejects (when configured)
3. Reception/Security checks in → visitor pass with Visit Number
4. Security/Reception verifies by Visit Number / search / check-out
5. Dashboards, search, reports, audit log

## Documentation

- [INSTALLATION.md](docs/INSTALLATION.md)
- [DEPLOYMENT_WINDOWS_SERVER.md](docs/DEPLOYMENT_WINDOWS_SERVER.md)
- [DATABASE.md](docs/DATABASE.md)
- [API.md](docs/API.md)
- [USER_GUIDE.md](docs/USER_GUIDE.md)
- [SECURITY.md](docs/SECURITY.md)

## License

Internal use — TIAANO.
