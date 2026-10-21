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

## Phase 3 tables

| Table | Purpose | Key constraints |
| --- | --- | --- |
| `Suppliers`, `StockLocations`, `InventoryItems` | who we buy from, where stock is kept, what we stock | unique code / SKU per branch |
| `StockBalances` | quantity and weighted-average cost per item and location | unique `(ItemId, LocationId)`; concurrency stamp; quantity can never go below zero (check constraint on SQL Server) |
| `StockMovements` | immutable stock ledger (receipt, issue, consumption, transfer, wastage, adjustment) | unique `(SourceType, SourceId, ItemId, LocationId, Type)` so a retried action moves stock once; updates and deletes are refused |
| `PurchaseRequisitions`, `PurchaseOrders`, `GoodsReceipts`, `SupplierInvoices`, `SupplierPayments` (+ lines) | procure-to-pay with three-way match | unique document numbers (`REQ-`, `PO-`, `GRN-`, `SINV-`, `SPAY-`); unique supplier invoice reference per supplier; idempotency keys on receipts and payments |
| `RestaurantOutlets`, `RestaurantTables`, `MenuCategories`, `MenuItems`, `MenuItemIngredients` | outlets and recipes | filtered unique index: one open order per table |
| `RestaurantOrders`, `RestaurantOrderLines`, `KitchenTickets`, `KitchenTicketLines` | orders with price snapshots and kitchen tickets per station | unique `ORD-` and `KOT-` numbers |
| `Assets`, `MaintenanceRequests`, `WorkOrders`, `WorkOrderPartUsages`, `PreventiveMaintenancePlans`, `PreventiveMaintenanceOccurrences` | maintenance | unique `WO-` numbers; one occurrence per plan and due date; part issues are idempotent |
| `Departments`, `Employees`, `WorkShifts`, `RosterEntries`, `AttendanceRecords`, `LeaveRecords`, `OvertimeRequests`, `ShiftHandovers` | staff | one employee per user per branch; filtered unique index: one open attendance session per employee |
| `Accounts`, `AccountMappings`, `AccountingPeriods` | company chart of accounts, posting-key mapping, monthly periods | unique account code and mapping key per company |
| `JournalEntries`, `JournalEntryLines` | immutable general ledger | unique `JE-` number; filtered unique `(BranchId, SourceType, SourceId)` so each business event posts once; lines and entries cannot be edited or deleted (only the reversal link is set) |

`Folios` gained `Type` (Guest or WalkIn) and `Name`; `ReservationId` and `GuestId` are now nullable so a
walk-in restaurant sale has its own folio.

### How operations post to the ledger

| Event | Journal |
| --- | --- |
| Room nights at check-in, folio charges | Dr Guest AR / Cr revenue and Tax Payable |
| Guest payment, refund | Dr Cash, POS settlement or Bank / Cr Guest AR (reverse for refunds) |
| Goods receipt | Dr Inventory / Cr Goods Received Not Invoiced |
| Supplier invoice (matched, or approved discrepancy) | Dr GRNI, Dr/Cr Purchase Price Variance / Cr Accounts Payable |
| Supplier payment | Dr Accounts Payable / Cr Bank or Cash |
| Kitchen send, parts issued, wastage, count differences | Dr Food Cost, Repairs or Inventory Adjustments / Cr Inventory |

Known simplifications, to review with the hotel's accountant: guest deposits stay in Guest AR rather than a
deposits account; room revenue is recognised at check-in; ingredients of voided food already sent stay
consumed; supplier returns and credit notes are not built yet; cash supplier payments are not tied to a
cashier shift; a consolidated trial balance covers only the branches the user can access.
