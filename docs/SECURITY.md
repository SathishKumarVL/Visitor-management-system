# Security — Secret Management (Phase 1A)

This document covers **credential and secret handling only**.  
It does **not** claim the application is fully production-hardened.

## Principles

1. Never commit secrets to Git.
2. Fail closed if required secrets are missing.
3. No hardcoded cryptographic fallbacks in source.
4. Never reset existing user passwords on application startup.
5. Never log passwords, JWT keys, encryption keys, SMTP passwords, or full ID numbers.

## Configuration sources (preferred order)

ASP.NET Core configuration stack:

1. `appsettings.json` — non-secret defaults only  
2. `appsettings.{Environment}.json` — non-secret environment defaults  
3. **User Secrets** (Development)  
4. **Environment variables** / secret store (Staging/Production)  
5. Future: Azure Key Vault, AWS Secrets Manager, HashiCorp Vault (via standard configuration providers)

Business code must not hardcode a specific vault vendor.

## Required secrets

| Key | Purpose | Notes |
|-----|---------|--------|
| `Jwt:Key` | JWT HMAC signing | ≥ 32 bytes entropy; rotate invalidates sessions |
| `Security:DataProtectionKey` | AES key material for visitor ID encryption | **Do not rotate blindly** — existing `VisitorDocuments.IdNumberEncrypted` depends on it |
| `Seed:DefaultPassword` | First-time seed user create only | Never used to reset existing users |
| `Smtp:Password` | SMTP AUTH | Optional if SMTP disabled / no username |

Non-secret SMTP fields (`Host`, `Port`, `Username`, `FromAddress`) may live in tracked config.

## Local Development setup

```powershell
cd backend
dotnet user-secrets set "Jwt:Key" "<generate-a-long-random-string>"
dotnet user-secrets set "Security:DataProtectionKey" "<must-match-key-used-to-encrypt-existing-IDs>"
dotnet user-secrets set "Seed:DefaultPassword" "<only-for-first-time-user-create>"
dotnet user-secrets set "Smtp:Password" "<rotated-smtp-password>"
```

Or set environment variables:

- `Jwt__Key`
- `Security__DataProtectionKey`
- `Seed__DefaultPassword`
- `Smtp__Password`

## Production

- Provide secrets via environment variables or an enterprise secret store.
- `Smtp:IgnoreSslErrors` **must be false** (startup fails if true in Production).
- Connection strings with passwords must not be committed; use env/secret store.
- `appsettings.Production.json` must not contain `Jwt:Key`, `Security:DataProtectionKey`, `Seed:DefaultPassword`, or `Smtp:Password`.

## Encryption key rotation

Current design: single key string → SHA-256 → AES.

Existing encrypted visitor ID numbers are bound to the current key.

**Safe rotation (future Phase):**

1. Add key version metadata (`KeyVersion` / algorithm id) to document storage.
2. Encrypt new records with Key N+1.
3. Background-migrate decrypt(old) → encrypt(new).
4. Retire old key only after verification.

Until that exists: keep the existing encryption key in the secret store even if it was previously exposed in Git, then plan a controlled re-encryption. Prefer rotating **JWT** and **SMTP** passwords immediately.

## SMTP

- Outbox pattern preserved; email failure must not corrupt visit state.
- TLS certificate validation required in Production.
- Development may set `Smtp:IgnoreSslErrors=true` only for broken corporate lab certs — never Production.

## Seed accounts

- Created only when missing.
- Require `Seed:DefaultPassword` only for create.
- **Never** reset passwords on startup.
- Prefer `MustChangePassword` for non-superadmin seed accounts.

## Git policy

- Baseline `v0.1.0-audit-baseline` contained secrets; history was rewritten in Phase 1A.
- Keep a local `git bundle` backup of the pre-clean baseline offline if needed.
- Do not push secret-bearing history to any remote.
- Recommended future CI: Gitleaks / TruffleHog / platform secret scanning.

## What Phase 1A does **not** cover

Public uploads, settings auth, rate limiting, MFA, refresh tokens, multi-tenancy, licensing.
QR functionality has been removed from the product.
