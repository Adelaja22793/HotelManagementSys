# Delivery roadmap

| Phase | Scope | Status |
| --- | --- | --- |
| 1. Foundation | Solution, identity, JWT + refresh, roles/permissions, branch isolation, EF Core + migrations, audit, health, error contract, WPF sign-in shell, CI | **Done** |
| 2. Core operations | Room types, rooms, rates and taxes, guests, availability and room-night inventory, reservations, check-in/out, housekeeping, folios, payments, receipts, refunds, cashier shifts | **Done** (API; desktop screens follow with phase 3) |
| 3. Operations / finance | POS, kitchen, room service, inventory, purchasing, maintenance, staff, accounting/GL | **Done** (API; desktop screens follow) |
| 4. Integration | Night audit and business-day close, analytics snapshots and dashboards, notification outbox (email, SMS, in-app), private documents with malware scanning and signatures, security log, lost and found, visitors, complaints, emergencies, audit search and export | **Done** (API; desktop screens follow) |
| 5. Release hardening | Public booking API (holds, Paystack, webhooks) and React booking website, security tests, performance tests against the p95 targets, restore verification, signed desktop updates with rollback, forwarded-header and token hardening | **Done** (API, website, tooling) |
| Next | WPF screens for the phase 2 to 4 modules (the API is complete; the desktop app has sign-in, branch choice and the module shell), pilot at one branch | Planned |

Each module follows the blueprint's pattern: domain model and invariants → DTOs and validation →
application service → EF configuration, constraints and migration → API with permission and branch
checks → WPF screen → unit, integration, concurrency and security tests → docs.
