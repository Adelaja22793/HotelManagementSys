# Database schema

Source of truth: EF Core model in `src/HMS.Infrastructure/Persistence` and migrations in
`Persistence/Migrations`. CI fails if the model changes without a migration.

## Phase 1 tables

| Table | Key columns | Constraints |
| --- | --- | --- |
| `Companies` | Id, Name, Code, DefaultCurrency, IsActive, ConcurrencyStamp | unique `Code` |
| `Branches` | Id, CompanyId, Code, Name, TimeZoneId, Currency, BusinessDayCutoffLocal, IsActive | unique `(CompanyId, Code)` |
| `HotelUserBranches` | UserId, CompanyId, BranchId, IsDefault, IsActive | unique `(UserId, BranchId)` |
| `AuditLogs` | OccurredAtUtc, CompanyId, BranchId, UserId, Action, EntityType, EntityId, ChangedProperties, TraceId | index `(BranchId, OccurredAtUtc)`, `(EntityType, EntityId)` |
| `RefreshTokens` | UserId, TokenHash, ExpiresAtUtc, RevokedAtUtc, ReplacedByTokenHash | unique `TokenHash` |
| `AspNetUsers` (+ Identity tables) | adds FullName, CompanyId, IsActive, MustChangePassword, LastLoginAtUtc | index `CompanyId` |

Every audited table also has `CreatedAtUtc`, `UpdatedAtUtc`, `CreatedByUserId`, `UpdatedByUserId`.
Foreign keys use `RESTRICT` (no cascade) except refresh tokens, which are removed with their user.
