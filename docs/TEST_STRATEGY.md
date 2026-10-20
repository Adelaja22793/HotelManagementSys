# Test strategy

| Project | Runs against | Focus |
| --- | --- | --- |
| `HMS.Domain.UnitTests` | memory | invariants, business date, codes, currency |
| `HMS.Application.UnitTests` | SQLite in-memory (real SQL) | branch access rules, auditing, permission map |
| `HMS.Api.IntegrationTests` | full HTTP pipeline + SQLite | auth, refresh rotation, revocation, cross-branch denial, error contract, rate limit |
| `HMS.Infrastructure.IntegrationTests` | **real SQL Server** | migrations, unique constraints, concurrency tokens |
| `HMS.Desktop.Tests` | Windows | ViewModels and API client behaviour |

SQLite is used only where provider behaviour does not matter. Locking, unique-index races and
transaction behaviour (for example the "two bookings for the last room" test in phase 2) must be
proven against SQL Server. CI starts a disposable SQL Server container; tests create and drop their own
database and never touch production.
