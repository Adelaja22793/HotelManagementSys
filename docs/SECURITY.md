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

## Added in phase 4

| Control | Where |
| --- | --- |
| Uploaded files are checked by extension, declared type and signature bytes (pdf, png, jpg, docx, xlsx only), limited to 10 MB, renamed to random keys and stored outside the web root | `FileRules`, `FileSystemDocumentStorage` |
| Files stay in quarantine until a malware scan (ClamAV) passes; a scanner outage keeps them quarantined, never released | `DocumentScanProcessor`, `ClamAvScanner` |
| Contracts, supplier invoices and policies need approval by someone other than the uploader | `DocumentVersion.Decide` |
| Classification limits who can see a document (Confidential: manage or approve; Restricted: approve); hidden documents look like they do not exist | `DocumentService` |
| Every document download and every audit export is written to the audit trail | `DocumentService`, `AuditQueryService` |
| Downloads are sent with `nosniff` and `no-store` | `DocumentsController` |
| Signatures are an HMAC over the signer, purpose, time and the file's SHA-256; a changed row or file no longer verifies | `HmacDocumentSigner` |
| Legal hold and retention dates stop deletion | `DocumentRecord.Delete` |
| Audit CSV export neutralises spreadsheet formulas (CSV injection) | `AuditQueryService.Cell` |
| Releasing found property needs `security.manage`; returns record who received it and the ID checked | `SecurityService`, `LostFoundItem` |
| Notification bodies and recipients are never logged; failed sends keep only a short provider error | `NotificationDispatcher` |

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
