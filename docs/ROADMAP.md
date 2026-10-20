# Delivery roadmap

| Phase | Scope | Status |
| --- | --- | --- |
| 1. Foundation | Solution, identity, JWT + refresh, roles/permissions, branch isolation, EF Core + migrations, audit, health, error contract, WPF sign-in shell, CI | **Done** |
| 2. Core operations | Room types, rooms, rates and taxes, guests, availability and room-night inventory, reservations, check-in/out, housekeeping, folios, payments, receipts, refunds, cashier shifts | **Done** (API; desktop screens follow with phase 3) |
| 3. Operations / finance | POS, kitchen, room service, inventory, purchasing, maintenance, staff, accounting/GL | Planned |
| 4. Integration | Documents, notification outbox, analytics snapshots, business-day close | Planned |
| 5. Release hardening | Public booking API and website, security/performance/restore tests, deployment and updater | Planned |

Each module follows the blueprint's pattern: domain model and invariants → DTOs and validation →
application service → EF configuration, constraints and migration → API with permission and branch
checks → WPF screen → unit, integration, concurrency and security tests → docs.
