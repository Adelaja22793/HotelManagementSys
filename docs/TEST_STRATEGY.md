# Test strategy

| Project | Runs against | Focus |
| --- | --- | --- |
| `HMS.Domain.UnitTests` | memory | invariants, business date, codes, currency |
| `HMS.Application.UnitTests` | SQLite in-memory (real SQL) | branch access rules, auditing, permission map |
| `HMS.Api.IntegrationTests` | full HTTP pipeline on SQLite, or SQL Server when `HMS_SQLSERVER_TEST_CONNECTION` is set | auth, refresh rotation, cross-branch denial, error contract, rate limit, and the full booking → check-in → payment → check-out → housekeeping flow, refunds, cashier variance, early departure, seasonal rates; phase 3 procure-to-pay with a price discrepancy, walk-in and room-charge restaurant orders, stock costing, work orders that block rooms, roster rules, attendance, manual journals and period close, each checked against the trial balance |
| `HMS.Infrastructure.IntegrationTests` | **real SQL Server** | migrations, unique constraints, concurrency tokens |
| `HMS.SecurityTests` | full HTTP pipeline (SQLite or SQL Server) and plain .NET | endpoint permission inventory, cross-branch sweep of every branch route, forged tokens, privilege escalation, document storage paths, desktop updater signatures and extraction |
| `HMS.PerformanceTests` | **SQL Server only** | p95 of login, availability, guest search, booking, folio and dashboard against the blueprint targets; a burst of bookings for the last rooms; a 20-second mixed workload |
| `HMS.Desktop.Tests` | Windows | ViewModels and API client behaviour |
| `booking-web` (Vitest) | jsdom | search and guest validation, token storage, the search → hold → guest → payment → confirmation flow with a mocked API |

SQLite is used only where provider behaviour does not matter. Locking, unique-index races and
transaction behaviour (for example the "two bookings for the last room" test in phase 2) must be
proven against SQL Server. CI starts a disposable SQL Server container; tests create and drop their own
database and never touch production.

Concurrency tests (`ConcurrencyTests`) run only on SQL Server: 12 parallel bookings for the last room
must produce exactly one reservation and 11 clean 409 responses, and 8 parallel copies of the same
payment must record it once.

Phase 3 adds a stock race: 4 parallel issues of 6 units against 10 on hand must leave exactly one success,
clean 409s for the rest and 4 units in stock and on the ledger.

Phase 4 tests (`IntegrationFeatureTests`) run a full night audit (blocked by a pending arrival, the exception
accepted with a reason, closed once, retry returns the same close, the next day refused until it starts),
check dashboard totals are weighted and missing days are reported, send a reservation email through the
pickup folder exactly once, show a message without a provider failing and being retried or cancelled, take a
contract through quarantine, scan, approval by someone else, download, signature, legal hold and retention,
reject an infected file, keep files quarantined during a scanner outage, hide restricted documents, and walk
incidents, lost property, complaints and visitors through their rules. A fake scanner flags any file containing
`EICAR-TEST`, and background jobs are run on demand through `/system/jobs/run`. Unit tests cover the file-type
rules and CSV escaping.

Phase 5 tests (`PublicBookingTests`) take a booking through hold, guest details, payment and confirmation, check
held rooms block both the website and the front desk, expire holds, rebook a payment that arrives after expiry,
send it to review when the rooms are gone, reject a wrong amount or a bad webhook signature, and race two
customers for the last room on SQL Server. `RestoreTests` back up the test database with checksums, restore it
under a new name beside a copy of the documents, open the API on the copy and require the restore check to pass
with identical row counts; a deleted file and a stray file are both reported.

Last local run of `HMS.PerformanceTests` (in-process host, SQL Server 2022 in Docker, so a floor rather than a
production benchmark):

| Operation | p95 | Target |
| --- | ---: | ---: |
| Login | 107 ms | 1 s |
| Room availability search | 17 ms | 1 s |
| Guest search | 12 ms | 750 ms |
| Reservation creation | 41 ms | 2 s |
| Folio and payment history | 15 ms | 1 s |
| Standard dashboard | 9 ms | 2 s |

25 parallel bookings for 3 rooms created exactly 3 with no server errors. 8 workers for 20 seconds made 3,118
requests (p95 170 ms) with no errors. Repeat the run on the production hardware with production-sized data before
go-live.
