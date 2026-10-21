# Deployment

## Topology

- API on a secured Windows Server (IIS or Windows service) with a trusted certificate, e.g. `https://hms-api.hotel.local/`.
- SQL Server on a separate secured host; firewall allows port 1433 **from the API host only**.
- Workstations get the desktop app and the API URL; they have no database credentials.

## Configuration (environment variables on the API host)

| Setting | Example |
| --- | --- |
| `ConnectionStrings__HotelManagementDb` | `Server=sql01;Database=HotelManagementDb;Integrated Security=True;Encrypt=True` |
| `Jwt__SigningKey` | 32+ random bytes from your secret store |
| `Database__ApplyMigrationsOnStartup` | `false` |
| `Bootstrap__Enabled` / `Bootstrap__AdminEmail` / `Bootstrap__AdminPassword` | first install only, then remove |
| `Desktop__MinimumVersion` / `Desktop__LatestVersion` | `0.1.0` |

### Phase 4 settings

| Setting | Notes |
| --- | --- |
| `Documents__StoragePath` | private folder outside the web root, e.g. `D:\HMS\documents`; back it up with the database |
| `Documents__ClamAvHost` / `Documents__ClamAvPort` | ClamAV daemon (`clamd`) reachable from the API host only, port 3310. Without it uploads stay quarantined |
| `Documents__AllowUnscanned` | `false` in production. Development only |
| `Documents__SignatureKey` | optional dedicated signature key; otherwise derived from `Jwt__SigningKey` (rotating that key then invalidates old signatures) |
| `Email__Mode` | `Smtp`, `Pickup` (writes .eml files, for testing) or `None` |
| `Email__Host`, `Email__Port`, `Email__Security`, `Email__Username`, `Email__Password`, `Email__FromAddress`, `Email__FromName` | SMTP relay. `Security` is `StartTls` (587), `SslOnConnect` (465) or `None` |
| `Sms__Provider`, `Sms__ApiKey`, `Sms__SenderId` | `Termii` or `None`; the sender id must be registered with Termii |
| `BackgroundJobs__Enabled`, `BackgroundJobs__IntervalSeconds` | the API sends notifications and scans files every 30 seconds by default. Several API servers can run it at once; work is claimed with database leases |
| `Security__LostFoundHoldDays`, `Security__VisitorRetentionDays` | 90 and 365 by default |

Messages for a channel with no provider fail visibly in the outbox and can be retried from
`notifications/outbox` once the provider is configured.

## Release steps

1. `deploy/scripts/publish-api.ps1 -Version x.y.z` and `deploy/scripts/publish-desktop.ps1 -Version x.y.z`.
2. `deploy/database/generate-migration-script.ps1`, review the SQL, rehearse on staging.
3. Take a full backup, apply the script, deploy the API into a new versioned folder, switch over, check `/health/ready`
   and `/api/v1/system/version`.
4. Keep the previous release folder for rollback. Schema changes follow expand-and-contract so the previous
   API version still works against the new schema.
