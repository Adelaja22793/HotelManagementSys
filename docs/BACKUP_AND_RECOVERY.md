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
- Private document storage (phase 4) is backed up separately with a catalogue tying files to metadata.
