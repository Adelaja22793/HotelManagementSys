# Security

## Implemented (phase 1)

| Control | Where |
| --- | --- |
| Short-lived JWT access tokens (15 min default) | `TokenService`, `Jwt:*` settings |
| Rotating refresh tokens, SHA-256 hashed at rest, reuse revokes the whole family | `IdentityService.RefreshAsync` |
| Immediate revocation on deactivate / role change / password change (security stamp check) | `Program.cs` `OnTokenValidated` |
| Lockout after 5 failed sign-ins (15 min) | Identity options |
| Generic "email or password is incorrect" for every sign-in failure | `IdentityService.LoginAsync` |
| Rate limiting on auth endpoints (per IP) | `RateLimitPolicies.Auth` |
| Deny-by-default authorization (fallback policy) + permission policies | `PermissionAuthorization.cs` |
| Company and branch isolation; unknown and foreign branches both return 403 | `BranchAccessService`, `BranchAccessFilter` |
| First admin via controlled bootstrap from secrets, forced password change | `DatabaseInitializer` |
| Audit trail for entity changes and user-access changes (no values stored) | `AuditingInterceptor`, `IdentityService` |
| Stable error contract with no stack traces or internals | `ExceptionHandlingMiddleware` |
| HTTPS required for the desktop API URL | `App.xaml.cs` |
| Dependency vulnerability scan in CI, vulnerable transitive packages pinned | `ci.yml`, `Directory.Packages.props` |

## Added in phase 3

| Control | Where |
| --- | --- |
| Separation of duties: requisitions, purchase orders, invoice discrepancies, leave and overtime cannot be approved by the person who raised them | domain entities (`SELF_APPROVAL`) |
| Work orders are signed off by someone other than the assigned technician | `MaintenanceService.InspectAsync` |
| Voiding food already sent to the kitchen needs `restaurant.manage` | `RestaurantService` |
| Ledger and stock movements are append-only; corrections are reversing entries | `AuditingInterceptor` |
| Manual journals cannot touch control accounts (AR, AP, inventory, GRNI) and closed periods refuse postings | `AccountingService`, `LedgerService` |

## Rules for every new module

- Put branch-scoped endpoints under `/api/v1/branches/{branchId}/...` so the global filter applies.
- Load records with both `Id` **and** `BranchId`; never trust a client-supplied `BranchId` or `CompanyId` alone.
- Apply the same checks to exports, file downloads, reports, background jobs and push events.
- Use `[HasPermission(...)]` on every controller or action; add new permissions to `Permissions` and map them.
- Never log passwords, tokens, card data, payment secrets or document contents.
- Retryable financial/booking actions take an `Idempotency-Key` header and enforce it with a unique index.

## Secrets

`Jwt:SigningKey`, connection strings and bootstrap credentials come from user-secrets (dev) or environment
variables / a secret store (production). `appsettings.json` contains empty placeholders only.
