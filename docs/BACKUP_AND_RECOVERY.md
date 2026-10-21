# Backup and recovery

| Workload | Example RPO | Example RTO |
| --- | --- | --- |
| Reservations / payments / accounting | 15 minutes | 2 hours |
| Private documents | 1 hour | 4 hours |
| Analytics snapshots | 24 hours | 8 hours |

These are targets to agree with the hotel, not guarantees.

- Switch the database to the FULL recovery model, then take a full backup to start the log chain.
- Run `deploy/database/backup.sql` with `sqlcmd -v Kind=FULL|DIFF|LOG` on a schedule (daily / 4-6h / 15 min).
- Copy backups to independent offsite storage with separate credentials and retention.
- `RESTORE VERIFYONLY` checks the file; it does not prove recoverability. Restore into an isolated server
  at least monthly (including a `STOPAT` point-in-time restore) and record the result.
- Back up private document storage separately: `deploy/database/backup-documents.ps1 -StoragePath D:\HMS\documents
  -BackupRoot E:\HMSBackups\documents` copies it to a timestamped folder with a SHA-256 catalogue. Run it hourly,
  after the log backup, so the files are never older than the database that points to them.

## Restore rehearsal

`deploy/database/restore-test.ps1` does the whole rehearsal on a non-production server:

1. `RESTORE VERIFYONLY ... WITH CHECKSUM` on every backup file.
2. Restores the full, differential and log backups (optionally `-StopAt` a point in time) into a database whose
   name must end in `_RestoreTest`, then runs `DBCC CHECKDB`.
3. Copies a document backup into a private work folder.
4. Runs `dotnet HMS.Api.dll verify-restore` against the copy and saves the JSON report. It checks that every document
   version has its file in the right area with the recorded SHA-256, lists stored files no record points to, checks
   every journal entry balances and that no migration is missing. Exit code 0 means consistent, 2 means problems,
   3 means the database could not be opened.
5. Drops the test database unless `-Keep` is given.

A system administrator can run the same check on the live system with `GET /api/v1/system/restore-check`.
Stray files alone do not fail the check (an upload interrupted before its record was saved leaves one); missing or
altered files, unbalanced journals and missing migrations do. Keep each report with the date, backup files used,
time taken (compare it to the RTO) and who ran it.
