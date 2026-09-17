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
| Visitors | Visitor profile |
| VisitorVisits | Visit instances + status |
| VisitorVisitPurposes | M:N visit ↔ purpose |
| VisitorVisitLocations | M:N visit ↔ location |
| VisitorPhotos | Stored photo paths |
| VisitorDocuments | Masked/encrypted ID data |
| Approvals | Approval history |
| VisitorPasses | Issued pass records (legacy PassCode retained; Visit Number is the public identifier) |
| SystemSettings | Configurable settings (unique per TenantId + Key) |
| NotificationOutbox | Notification abstraction queue (tenant-scoped; nullable `TenantId` for pre-existing rows) |
| EmergencyRollCallEvents | Append-only evacuation roll call; never mutates the visit |
| VisitorFaceDescriptors | Face templates for returning-visitor recognition |
| Tenants / Sites | Multi-tenant foundation |
| ProductModules / TenantModuleEntitlements / TenantLicenses | Productization scaffold |
| FeatureFlags / ApplicationReleases | Flags + release history |

## Tenant scope

All operational masters and transactional tables carry `TenantId` and EF global query filters:

Departments, Employees, AspNetUsers, VisitPurposes, Locations, IdTypes, Visitors, VisitorVisits, SystemSettings, Sites.

Approvals, passes, photos, and documents are isolated indirectly via filtered parent visits/visitors.

## Indexes

- Visitors: (TenantId, VisitorNumber) unique, FullName, Phone, CompanyName
- VisitorVisits: (TenantId, VisitNumber) unique, Status, VisitDate, PreRegistrationReference
- IdTypes: (TenantId, Name)
- VisitorPasses: PassCode (unique)
- SystemSettings: (TenantId, Key) unique

## Soft delete policy

Master data is **deactivated** (`IsActive = false`), never hard-deleted when referenced by historical visits.

## Migrations

```powershell
cd backend
dotnet ef migrations add <Name> --output-dir Data/Migrations
dotnet ef database update
```

Notable migrations: `AddMultiTenantFoundation`, `AddProductizationFoundation`, `TenantScopedMastersAndIsolation`.

Startup also calls `Database.MigrateAsync()` then seeds reference data.

## Seed reference data

- Default tenant TIAANO (`11111111-1111-1111-1111-111111111111`)
- 12 departments (Managing Director … MSE)
- 13 visit purposes (Equipments … ETP/STP)
- 10 locations (Anode Hall … Others)
- ID types (tenant-scoped)
- Sample hosts and role users
- Default entitlements: visitor-management + emergency-management enabled; analytics not entitled
