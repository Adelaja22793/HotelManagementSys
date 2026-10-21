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

## Added in phase 5

| Control | Where |
| --- | --- |
| Public booking is anonymous but rate limited per client and restricted to listed origins (CORS); the Paystack webhook takes no CORS and a 64 KB body | `PublicBookingController`, `RateLimitPolicies`, `Program.cs` |
| Booking access tokens are 32 random bytes, stored only as SHA-256 and compared in fixed time; a wrong token looks like an unknown booking (404). The website keeps them in the URL fragment and local storage, never cookies | `BookingHold`, `booking-web/src/services/tokens.ts` |
| Payments are confirmed only by server-side verification or a webhook signed with HMAC-SHA512; amount, currency and reference must all match or the payment goes to review | `PublicBookingService`, `PaystackGateway` |
| The sandbox payment provider refuses to start outside Development | `Program.cs` |
| Holds per client are capped and expire; a honeypot field quietly drops bot submissions | `PublicBookingOptions`, `GuestDetailsRequest` |
| Security headers on every response (CSP `default-src 'none'`, `nosniff`, `DENY` framing, `no-referrer`, COOP) and `no-store` on API responses; the server header is removed | `SecurityHeadersMiddleware` |
| Only HS256 access tokens are accepted; edited, unsigned (`alg: none`) and foreign-key tokens fail | `Program.cs`, `TokenTamperingTests` |
| A user can only grant roles whose permissions they already hold, and only edit users whose roles they could grant | `RolePermissions.CanGrant`, `IdentityService` |
| `X-Forwarded-For` is trusted only from `ForwardedHeaders:KnownProxies`, so clients cannot dodge rate limits with a fake header | `Program.cs` |
| Desktop updates: ECDSA P-256 signed manifest from trusted keys, newer versions only, HTTPS from trusted hosts, package size and SHA-256 checked, extraction refuses `..`, absolute paths, links and zip bombs, side-by-side install with rollback, the manifest cannot run commands | `HMS.Updater`, `HMS.Launcher` |

Automated checks in `HMS.SecurityTests`: every endpoint either has a permission or is on a reviewed anonymous list;
a manager of one branch is refused on every branch endpoint of another (all methods, generated from the route table)
and cannot reach another branch's records through their own routes; forged tokens; document storage keys; risky
file types; role granting; and the updater rules above.

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
