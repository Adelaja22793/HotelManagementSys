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

### Phase 5 settings

| Setting | Notes |
| --- | --- |
| `PublicBooking__PaymentProvider` | `Paystack` in production. `Sandbox` confirms without money and is refused outside Development |
| `Paystack__SecretKey` | from the Paystack dashboard, in the secret store. Set the dashboard webhook URL to `https://<api host>/api/v1/public/payments/paystack/webhook` |
| `PublicBooking__SiteUrl` | the booking website, e.g. `https://book.hotel.com`; Paystack returns guests there after paying |
| `PublicBooking__AllowedOrigins__0` | the website's origin (exact, no trailing slash). Leave empty to refuse all browser calls |
| `PublicBooking__HoldMinutes`, `PublicBooking__MaxActiveHoldsPerClient`, `PublicBooking__MaxDaysAhead` | 15, 3 and 365 by default |
| `PublicBooking__PaymentMode` | `Full`, or `Deposit` to charge the rate plan's deposit when it has one |
| `PublicBooking__CompanyCode` | limits the public site to one company when the database holds several |
| `RateLimiting__PublicReadPerMinute`, `RateLimiting__PublicWritePerMinute` | 60 and 10 per client address |
| `ForwardedHeaders__KnownProxies__0` | the reverse proxy's IP address. Without it the API sees the proxy's address for every guest and rate limits them together; never list addresses you do not control |

Turn public booking on per branch with `PUT /api/v1/branches/{id}/public-booking` (`{ "enabled": true }`).

### Booking website

`booking-web` builds to static files (`npm ci && npm run build` → `dist/`). Host them on any static host or CDN over
HTTPS, with every unknown path served `index.html`. Set `VITE_API_BASE_URL` at build time when the API is on
another host. Recommended response headers for the site:

```
Content-Security-Policy: default-src 'self'; connect-src 'self' https://<api host>; img-src 'self' data:; style-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self' https://checkout.paystack.com
Referrer-Policy: no-referrer
X-Content-Type-Options: nosniff
Strict-Transport-Security: max-age=31536000
```

### Desktop updates

1. Once, on the release machine: `dotnet run --project tools/HMS.ReleaseTool -- keygen --out <secure folder> --key-id hms-2026`.
   Keep the private key in the release secret store. Put the public PEM in `Updates:TrustedKeys:hms-2026` in the
   desktop `appsettings.json`, the update host in `Updates:TrustedPackageHosts`, and the manifest address in
   `Updates:ManifestUrl`.
2. Install the first version per workstation into a folder only administrators can write, e.g.
   `C:\Program Files\HMS\`, with the launcher `HMS.exe` at the top and the app under `versions\<version>\`
   (`current.json` names the active version). The shortcut points at `HMS.exe`.
3. Each release: `deploy/scripts/publish-desktop.ps1 -Version x.y.z -PackageUrl https://<update host>/hms/x.y.z.zip
   -SigningKey <private.pem> -KeyId hms-2026`, sign the executables with the publisher certificate before packaging,
   then upload the zip and `manifest.json`.
4. The app checks after sign-in, downloads and verifies in the background, stages the new version beside the old one
   and offers a restart. If the new version will not start, the launcher falls back to the previous one.
   Rotate keys by adding the new public key before signing with it.

## Release steps

1. `deploy/scripts/publish-api.ps1 -Version x.y.z` and `deploy/scripts/publish-desktop.ps1 -Version x.y.z`.
2. `deploy/database/generate-migration-script.ps1`, review the SQL, rehearse on staging.
3. Take a full backup, apply the script, deploy the API into a new versioned folder, switch over, check `/health/ready`
   and `/api/v1/system/version`.
4. Keep the previous release folder for rollback. Schema changes follow expand-and-contract so the previous
   API version still works against the new schema.
