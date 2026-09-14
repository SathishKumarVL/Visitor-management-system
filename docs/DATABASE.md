# Database Design

## Engine

Microsoft SQL Server via Entity Framework Core.

Database name (dev default): `TiaanoVms`

## Core tables

| Table | Purpose |
|-------|---------|
| AspNetUsers / AspNetRoles | Identity users & roles |
| Departments | TIAANO departments |
| Employees | Hosts / employees |
| VisitPurposes | Purpose master |
| Locations | Location master |
| IdTypes | ID document types |
| EntryGates / ExitGates | Gates |
| Visitors | Visitor profile |
| VisitorVisits | Visit instances + status |
| VisitorVisitPurposes | M:N visit ↔ purpose |
| VisitorVisitLocations | M:N visit ↔ location |
| VisitorPhotos | Stored photo paths |
| VisitorDocuments | Masked/encrypted ID data |
| Approvals | Approval history |
| VisitorPasses | QR pass codes (no PII in QR) |
| AuditLogs | Immutable audit trail |
| SystemSettings | Configurable settings |
| NotificationOutbox | Notification abstraction queue |

## Indexes

- Visitors: VisitorNumber (unique), FullName, Phone, CompanyName
- VisitorVisits: VisitNumber (unique), Status, VisitDate, PreRegistrationReference
- VisitorPasses: PassCode (unique)
- AuditLogs: CreatedAt, Entity, Action
- SystemSettings: Key (unique)

## Soft delete policy

Master data is **deactivated** (`IsActive = false`), never hard-deleted when referenced by historical visits.

## Migrations

```powershell
cd backend
dotnet ef migrations add <Name> --output-dir Data/Migrations
dotnet ef database update
```

Startup also calls `Database.MigrateAsync()` then seeds reference data.

## Seed reference data

- 12 departments (Managing Director … MSE)
- 13 visit purposes (Equipments … ETP/STP)
- 10 locations (Anode Hall … Others)
- ID types, entry/exit gates
- Sample hosts and role users
