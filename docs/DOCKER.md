# Docker / cloud readiness (foundation)

This stack remains on-premise first. The following compose file is a **local development aid**, not a production deployment.

```yaml
services:
  api:
    image: mcr.microsoft.com/dotnet/aspnet:9.0
    working_dir: /app
    volumes:
      - ./backend/publish:/app
      - vms-media:/app/App_Data/media
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__DefaultConnection: "Server=sql;Database=TiaanoVms;User Id=sa;Password=CHANGE_ME;TrustServerCertificate=True"
      Jwt__Key: "REPLACE_WITH_LONG_RANDOM_DEV_KEY_32+"
      Security__DataProtectionKey: "REPLACE_WITH_DEV_ENCRYPTION_KEY_16+"
    ports:
      - "8080:8080"
    depends_on:
      - sql

  sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "CHANGE_ME_StrongPassword1"
    ports:
      - "1433:1433"
    volumes:
      - vms-sql:/var/opt/mssql

volumes:
  vms-media:
  vms-sql:
```

## Cloud readiness checklist

- [x] Environment-based secret configuration abstraction
- [x] Stateless API + JWT
- [x] Private media storage path abstraction (tenant folders)
- [ ] Object storage provider (S3/Azure Blob)
- [ ] Managed identity / Key Vault provider wiring
- [ ] Horizontal scale sticky-session-free verification
- [ ] Containerized production hardened image build
