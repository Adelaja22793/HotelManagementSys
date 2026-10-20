# Test strategy

| Project | Runs against | Focus |
| --- | --- | --- |
| `HMS.Domain.UnitTests` | memory | invariants, business date, codes, currency |
| `HMS.Application.UnitTests` | SQLite in-memory (real SQL) | branch access rules, auditing, permission map |
| `HMS.Api.IntegrationTests` | full HTTP pipeline on SQLite, or SQL Server when `HMS_SQLSERVER_TEST_CONNECTION` is set | auth, refresh rotation, cross-branch denial, error contract, rate limit, and the full booking → check-in → payment → check-out → housekeeping flow, refunds, cashier variance, early departure, seasonal rates |
| `HMS.Infrastructure.IntegrationTests` | **real SQL Server** | migrations, unique constraints, concurrency tokens |
| `HMS.Desktop.Tests` | Windows | ViewModels and API client behaviour |

SQLite is used only where provider behaviour does not matter. Locking, unique-index races and
transaction behaviour (for example the "two bookings for the last room" test in phase 2) must be
proven against SQL Server. CI starts a disposable SQL Server container; tests create and drop their own
database and never touch production.

Concurrency tests (`ConcurrencyTests`) run only on SQL Server: 12 parallel bookings for the last room
must produce exactly one reservation and 11 clean 409 responses, and 8 parallel copies of the same
payment must record it once.
