# Delivery roadmap

| Phase | Scope | Status |
| --- | --- | --- |
| 1. Foundation | Solution, identity, JWT + refresh, roles/permissions, branch isolation, EF Core + migrations, audit, health, error contract, WPF sign-in shell, CI | **Done** |
| 2. Core operations | Room types, rooms, rates and taxes, guests, availability and room-night inventory, reservations, check-in/out, housekeeping, folios, payments, receipts, refunds, cashier shifts | **Done** (API and desktop screens) |
| 3. Operations / finance | POS, kitchen, room service, inventory, purchasing, maintenance, staff, accounting/GL | **Done** (API and desktop screens) |
| 4. Integration | Night audit and business-day close, analytics snapshots and dashboards, notification outbox (email, SMS, in-app), private documents with malware scanning and signatures, security log, lost and found, visitors, complaints, emergencies, audit search and export | **Done** (API and desktop screens) |
| 5. Release hardening | Public booking API (holds, Paystack, webhooks) and React booking website, security tests, performance tests against the p95 targets, restore verification, signed desktop updates with rollback, forwarded-header and token hardening | **Done** (API, website, tooling) |
| 6. Desktop screens | All 19 modules in the WPF app: dashboard, front desk, reservations, folios, cashier, housekeeping, rooms and rates, guests, restaurant/POS with kitchen tickets, inventory and purchasing, maintenance, staff and rosters, accounting, night audit, document centre, security log, users, audit log and Setup. Setup adds, lists and changes 23 kinds of master data (room types, rooms, rate plans, seasonal rates, taxes, services, departments, shifts, employees, stock items, stores, suppliers, outlets, tables, menus, assets, preventive plans, chart of accounts, posting accounts, branch details, message templates). Task buttons on each screen open a side form for 24 operations, such as receiving and moving stock, requisitions, purchase orders, supplier invoices and payments, room blocks, refunds and overtime. Each screen is tested against the real API | **Done** |
| Next | Pilot at one branch: real data load, staff training, Windows UI walkthrough of every screen, then roll-out | Planned |

Each module follows the blueprint's pattern: domain model and invariants → DTOs and validation →
application service → EF configuration, constraints and migration → API with permission and branch
checks → WPF screen → unit, integration, concurrency and security tests → docs.
