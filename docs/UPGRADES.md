# Upgrades

## Principles

1. Never partially upgrade a production database.
2. Backup before migration.
3. Validate health after update.
4. Do not destroy historical visitor data.
5. Keep API v1 compatible during minor upgrades.

## Suggested on-premise flow

1. Put application into maintenance mode (IIS app offline page / drain)
2. Backup SQL Server database
3. Backup `App_Data/media`
4. Deploy new binaries
5. Run EF migrations (`dotnet ef database update` or release script)
6. Hit `/api/system/health`
7. Confirm `/api/system/version`
8. Exit maintenance mode

## Compatibility checks

Before applying a release:

- Application version
- Required database migration id
- License edition / module entitlement (must not lock out existing data)

## Graceful license expiry

Expired licenses should not delete data. Prefer read-only or warning modes with configurable grace days.

## Rollback

Prefer restore-from-backup over destructive schema rollback.
