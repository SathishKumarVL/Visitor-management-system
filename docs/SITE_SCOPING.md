# Site Scoping (Multi-Workplace)

Sites are the second isolation axis inside a tenant. A tenant is the customer; a site is one of that
customer's workplaces — a plant, branch or office building.

## The rule

**A user bound to a site sees only that site. A user with no site sees the whole tenant.**

Enforcement follows the *assignment*, not the role. An Admin assigned to Chennai is restricted to
Chennai exactly like a receptionist would be. Cross-site visibility is granted by leaving `SiteId`
empty, which is how Admin and SuperAdmin accounts are expected to be configured.

## Where it is enforced

Scoping lives in EF Core global query filters, not in individual queries, so every read path inherits
it without having to be updated:

| Entity | Filter |
|---|---|
| `VisitorVisit` | tenant matches **and** (caller has no site **or** `SiteId` matches) |
| `Location` | tenant matches **and** (caller has no site **or** `SiteId` is null **or** `SiteId` matches) |

Because visitor search, the inside list, pending approvals, the dashboard, all four report formats and
the emergency roster all query `_db.VisitorVisits` without `IgnoreQueryFilters()`, they are scoped
automatically.

A `Location` with no site is shared by every site — useful for an area that genuinely exists at all of
them. The UI calls this "Shared by all sites".

### Deliberate exception: visit numbering

`NextVisitNumberAsync` calls `IgnoreQueryFilters()` and constrains by `TenantId` explicitly. Visit
numbers are unique per **tenant** (`IX_VisitorVisits_TenantId_VisitNumber`), so an allocator reading
through the site filter would restart at 1 for each site and collide. This is covered by
`New_Registration_Is_Stamped_With_Acting_Users_Site`.

### Deliberate exception: user administration

`ApplicationUser` is **not** site-filtered. Managing accounts is a tenant-level function: an admin must
be able to see and reassign users across sites, and a site-bound admin would otherwise be unable to see
unassigned colleagues. Site assignment restricts *visitor data*, not *account management*.

## How a visit gets its site

`SiteService.ResolveVisitSiteIdAsync()` returns the acting user's site when they have one, otherwise the
tenant default site. Both walk-in registration and expected-visitor creation stamp it. Existing rows
created before this feature are adopted by the default site in `DbSeeder.BackfillSiteIdsAsync`.

## Claim propagation

`SiteId` is issued as the `siteId` JWT claim at login and validated by `TenantResolutionMiddleware`
against the tenant (an unknown or inactive site is dropped). Changing a user's site therefore takes
effect when their access token is next refreshed — within the access-token lifetime
(`Jwt:AccessTokenMinutes`, default 30), or immediately if they sign out and back in.

## Business rules

Creating a site is capped by `TenantLicense.MaxSites` (seeded at 5). A site cannot be deactivated while
visitors are still inside it or active users are assigned to it, and the tenant must always keep at
least one active site and exactly one default. Site names and codes are unique per tenant.

## API

| Route | Method | Roles |
|---|---|---|
| `/api/sites?activeOnly=` | GET | any authenticated |
| `/api/sites` | POST | SuperAdmin, Admin |
| `/api/sites/{id}` | PUT | SuperAdmin, Admin |
| `/api/sites/{id}/deactivate` | POST | SuperAdmin, Admin |

`SiteDto` includes `userCount`, `locationCount` and `insideCount` so an admin can see the blast radius
before deactivating.

## UI

- **Sites** (`/masters/sites`, Admin) — create and edit workplaces, set the default, deactivate.
- **Users** (`/users`) — a Site dropdown on create and edit; the list shows "All sites" when unassigned.
- **Locations** (`/masters/locations`) — a Site dropdown nesting each area under a workplace.

## Tests

`backend.Tests/SiteScopingTests.cs` builds two sites **in the same tenant** so a pass cannot be
explained by tenant isolation. It covers search, the inside list, direct-by-id access (with a positive
control), dashboard counts, reports, the emergency roster, location scoping, site stamping on
registration, role enforcement on writes, duplicate names, and refusal to deactivate an occupied site.
