# Architecture

```
WPF desktop workstations ──HTTPS (hotel LAN)──▶ ASP.NET Core Web API ──▶ SQL Server
                                                 │                       (reachable by API only)
                                                 └──▶ private file storage, background workers (later phases)
```

Two ways to run it:

- **Local (default, one PC).** `HMS.Desktop` starts the API itself (`LocalServer` in `HMS.Desktop.Core`): the
  `server` folder next to the app holds `HMS.Api`, which listens on `127.0.0.1` on a free port and uses SQL Server
  Express LocalDB. Its JWT key is generated once and kept with Windows DPAPI in `%LocalAppData%\HMS\local-server.json`.
  The API watches the app's process id (`Hosting:ParentProcessId`) and stops when the app exits. A first-run screen
  supplies the bootstrap hotel and administrator. Staff only ever open one program.
- **Remote (several PCs, booking website).** `Api:Mode` is `Remote` and every desktop connects to one shared API over HTTPS.

## Layers

- **Domain** holds entities and invariants (e.g. `Branch.GetBusinessDate`, code/currency validation). It references nothing.
- **Contracts** holds DTOs only, so the desktop can share them without referencing server code.
- **Application** holds use cases, the permission model (`Roles`, `Permissions`, `RolePermissions`) and the
  single branch-access gate (`IBranchAccessService`). It talks to the database through `IApplicationDbContext`.
- **Infrastructure** implements persistence (EF Core, SQL Server), ASP.NET Core Identity, JWT issuing,
  auditing and first-run bootstrap.
- **Api** wires the HTTP pipeline: correlation ids, error contract, authentication, rate limiting,
  permission policies, the global branch-access filter and health checks.
- **Desktop** is MVVM. `HMS.Desktop.Core` holds the view models and the `ApiClient` (its only data access path) and has no
  WPF dependency, so every screen's logic is tested on any OS against the real API. `HMS.Desktop` holds the XAML views,
  one per view model, matched by `DataTemplate` in `App.xaml`. The module list in `ShellViewModel` is filtered by the
  signed-in user's permissions; the API still checks every call.

This is a modular monolith: one API deployment and one database, with modules separated by folder
and service boundaries. Split services only when a real scaling or operational need appears.

## Request pipeline

1. `CorrelationIdMiddleware` assigns `X-Correlation-Id` (returned as `traceId` in errors).
2. `ExceptionHandlingMiddleware` maps exceptions to `ApiError { code, description, traceId, errors }`.
3. JWT bearer authentication. `OnTokenValidated` rejects tokens of deactivated users or users whose
   security stamp changed (role change, password change), so access changes apply immediately.
4. Rate limiting on login/refresh/change-password.
5. Authorization: fallback policy requires an authenticated user; `[HasPermission]` maps to role permissions.
6. `BranchAccessFilter`: any route with `{branchId}` is checked for company match, active company/branch and
   membership before the action runs. Services re-check ownership of the specific records they load.

## Conventions

- GUID keys; UTC `DateTime` for events (re-tagged as UTC on read); branch time zone + cutoff for business dates.
- `decimal(18,2)` money by default; currency code stored where it may vary (NGN default).
- `IConcurrencyTracked.ConcurrencyStamp` is rotated on every update and used as an optimistic concurrency token.
- `AuditingInterceptor` writes `AuditLog` rows in the same `SaveChanges` as the change, recording property
  names only (never values).
- No cascade deletes on business data. Financial records are reversed, not deleted.
- Routes are versioned under `/api/v1/`. Branch-scoped resources live under `/api/v1/branches/{branchId}/...`.
