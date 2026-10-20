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

## Phase 2 tables

| Table | Purpose | Key constraints |
| --- | --- | --- |
| `RoomTypes`, `Rooms`, `RoomBlocks` | sellable types, physical rooms and their status, maintenance or group blocks | unique `(BranchId, Code)` and `(BranchId, RoomNumber)` |
| `RatePlans`, `RoomRates`, `TaxRules`, `ServiceItems` | prices by date range and weekday, taxes (inclusive or exclusive), chargeable services | unique plan code per branch |
| `Guests` | guest profiles | index on name, phone and email per branch |
| `RoomNightInventory` | one row per branch, room type and night: physical, blocked, sell limit, confirmed, held | unique `(BranchId, RoomTypeId, Date)`; check constraint keeps counts non-negative |
| `Reservations`, `ReservationNights` | booking with a price snapshot per night | unique reservation number; filtered unique `IdempotencyKey` |
| `AssignedRoomNights` | which physical room a reservation holds each night | unique `(RoomId, Date)`, so a room cannot be given to two guests |
| `Stays` | an in-house guest | filtered unique index: one active stay per room |
| `HousekeepingTasks` | cleaning and inspection workflow | unique source (one check-out task per stay) |
| `Folios`, `FolioCharges` | guest account and its lines; corrections are reversing lines, never edits | filtered unique `IdempotencyKey` |
| `Payments`, `Receipts`, `Invoices`, `Refunds` | money in and out; invoices are numbered snapshots | unique payment `IdempotencyKey` (required); unique document numbers |
| `CashierShifts`, `CashierMovements` | cash drawer per cashier and every cash movement | filtered unique index: one open shift per cashier |
| `BranchSequences` | gap-safe document numbering (`RES-`, `FOL-`, `RCT-`, `INV-`, `RFD-`) | primary key `(BranchId, Name)` |

Room availability is changed only by single conditional `UPDATE` statements that cover every night of
a stay, so two clerks booking the last room at the same moment cannot both succeed.
