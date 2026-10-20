# Hotel Management System (HMS)

Multi-branch hotel platform: a WPF desktop app for staff, a central ASP.NET Core Web API, and SQL Server.
Desktops never connect to the database; every read and write goes through the API, which enforces
authentication, permissions and branch isolation.

Built from the *Hotel Management System Master Blueprint* in delivery phases. See [docs/ROADMAP.md](docs/ROADMAP.md)
for what is implemented today.

## Solution layout

| Project | Purpose |
| --- | --- |
| `src/HMS.Domain` | Entities, invariants, domain rules. No dependencies. |
| `src/HMS.Contracts` | Request/response DTOs shared by API and desktop. |
| `src/HMS.Application` | Use cases, permissions, branch access, validation. |
| `src/HMS.Infrastructure` | EF Core (SQL Server), Identity, JWT, auditing, migrations. |
| `src/HMS.Api` | Controllers, middleware, auth, health checks. |
| `src/HMS.Desktop` | WPF client (MVVM, CommunityToolkit). Windows only. |
| `tests/*` | Unit, API integration, SQL Server integration and desktop tests. |

## Prerequisites

- .NET SDK 10.0
- SQL Server 2019+ (LocalDB is fine for development on Windows; Docker works elsewhere)
- Windows for running the desktop app (it builds anywhere)

## Run locally

```bash
dotnet tool restore

# 1. Secrets (never commit these)
cd src/HMS.Api
dotnet user-secrets set "Jwt:SigningKey" "<at least 32 random characters>"
dotnet user-secrets set "Bootstrap:AdminEmail" "admin@yourhotel.local"
dotnet user-secrets set "Bootstrap:AdminPassword" "<temporary password, 10+ chars>"
# Optional: point at your own SQL Server instead of LocalDB
dotnet user-secrets set "ConnectionStrings:HotelManagementDb" "Server=localhost,1433;Database=HotelManagementDb_Dev;User Id=sa;Password=...;TrustServerCertificate=True"

# 2. Start the API (Development applies migrations and creates the first company, branch and SystemAdmin)
dotnet run
```

SQL Server in Docker (non-Windows):

```bash
docker run -d --name hms-sql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=<strong password>' -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

Desktop: set `Api:BaseUrl` in `src/HMS.Desktop/appsettings.json` to the API's HTTPS address, then
`dotnet run --project src/HMS.Desktop` on Windows. The first admin sign-in is flagged to change the temporary password.

## Tests

```bash
dotnet test HotelManagementSystem.sln
```

- SQL Server tests run when `HMS_SQLSERVER_TEST_CONNECTION` is set, e.g.
  `Server=localhost,1433;User Id=sa;Password=...` (each run creates and drops its own database).
- `HMS.Desktop.Tests` runs on Windows only.

## Migrations

```bash
dotnet ef migrations add <Name> --project src/HMS.Infrastructure --startup-project src/HMS.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent --project src/HMS.Infrastructure --startup-project src/HMS.Infrastructure --output artifacts/database/deploy.sql
```

Production applies reviewed idempotent scripts; `Database:ApplyMigrationsOnStartup` stays `false`.

## Documentation

[Architecture](docs/ARCHITECTURE.md) · [Security](docs/SECURITY.md) · [API reference](docs/API_REFERENCE.md) ·
[Database](docs/DATABASE_SCHEMA.md) · [Testing](docs/TEST_STRATEGY.md) · [Deployment](docs/DEPLOYMENT.md) ·
[Backup and recovery](docs/BACKUP_AND_RECOVERY.md) · [Roadmap](docs/ROADMAP.md)
