# Test strategy

| Project | Runs against | Focus |
| --- | --- | --- |
| `HMS.Domain.UnitTests` | memory | invariants, business date, codes, currency |
| `HMS.Application.UnitTests` | SQLite in-memory (real SQL) | branch access rules, auditing, permission map |
| `HMS.Api.IntegrationTests` | full HTTP pipeline on SQLite, or SQL Server when `HMS_SQLSERVER_TEST_CONNECTION` is set | auth, refresh rotation, cross-branch denial, error contract, rate limit, and the full booking → check-in → payment → check-out → housekeeping flow, refunds, cashier variance, early departure, seasonal rates; phase 3 procure-to-pay with a price discrepancy, walk-in and room-charge restaurant orders, stock costing, work orders that block rooms, roster rules, attendance, manual journals and period close, each checked against the trial balance |
| `HMS.Infrastructure.IntegrationTests` | **real SQL Server** | migrations, unique constraints, concurrency tokens |
| `HMS.Desktop.Tests` | Windows | ViewModels and API client behaviour |

SQLite is used only where provider behaviour does not matter. Locking, unique-index races and
transaction behaviour (for example the "two bookings for the last room" test in phase 2) must be
proven against SQL Server. CI starts a disposable SQL Server container; tests create and drop their own
database and never touch production.

Concurrency tests (`ConcurrencyTests`) run only on SQL Server: 12 parallel bookings for the last room
must produce exactly one reservation and 11 clean 409 responses, and 8 parallel copies of the same
payment must record it once.

Phase 3 adds a stock race: 4 parallel issues of 6 units against 10 on hand must leave exactly one success,
clean 409s for the rest and 4 units in stock and on the ledger.
