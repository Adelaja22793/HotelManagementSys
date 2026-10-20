# API reference (v1)

All routes are prefixed with `/api/v1`. Errors always use:

```json
{ "code": "ROOM_NOT_AVAILABLE", "description": "…", "traceId": "…", "errors": { "field": ["message"] } }
```

Status codes: 400 validation, 401 unauthenticated, 403 forbidden / branch denied, 404 not found,
409 state or concurrency conflict, 429 rate limited, 5xx unexpected.

Lists are paged with `?page=1&pageSize=50` (max 200) and return `{ items, page, pageSize, totalCount, totalPages }`.

## Auth

| Method | Route | Access | Notes |
| --- | --- | --- | --- |
| POST | `/auth/login` | anonymous, rate limited | `{ email, password }` → tokens, user, permissions, branches |
| POST | `/auth/refresh` | anonymous, rate limited | `{ refreshToken }` → new token pair (old one revoked) |
| POST | `/auth/logout` | signed in | `{ refreshToken }` |
| GET | `/auth/me` | signed in | current user and permissions |
| POST | `/auth/change-password` | signed in | revokes all sessions; sign in again |

## Branches

| Method | Route | Permission |
| --- | --- | --- |
| GET | `/branches` | `branches.view` (only branches you can access) |
| GET | `/branches/{branchId}` | `branches.view` |
| POST | `/branches` | `system.manage` |
| PUT | `/branches/{branchId}` | `branches.manage` (send `concurrencyStamp`) |
| GET | `/branches/{branchId}/audit-logs` | `audit.view` |

## Users

| Method | Route | Permission |
| --- | --- | --- |
| GET | `/users` | `users.manage` |
| GET | `/users/{userId}` | `users.manage` |
| POST | `/users` | `users.manage` — temporary password, roles, branch ids |
| PUT | `/users/{userId}/access` | `users.manage` — roles, branches, active flag |
| GET | `/roles` | `users.manage` |

## System

| Method | Route | Access |
| --- | --- | --- |
| GET | `/system/version` | anonymous; migration state shown to SystemAdmin only |
| GET | `/health/live`, `/health/ready` | anonymous (no details) |

## Core operations (phase 2)

All routes below sit under `/branches/{branchId}` and are checked against the caller's branch access.
Records from another branch return 404. Updates send `concurrencyStamp`; a stale stamp returns
409 `CONCURRENCY_CONFLICT`. Money-moving POSTs (reservations, folio charges, payments) take an
`Idempotency-Key` header, and a retry with the same key returns the original result.

| Area | Routes | Permissions |
| --- | --- | --- |
| Rooms | `GET/POST room-types`, `PUT room-types/{id}`, `GET/POST rooms`, `PUT rooms/{id}`, `GET/POST room-blocks`, `POST room-blocks/{id}/release` | `rooms.view`, `rooms.manage` |
| Inventory | `GET inventory?start&end`, `PUT inventory/sell-limit`, `POST inventory/reconcile` | `rooms.view`, `rates.manage`, `rooms.manage` |
| Rates | `GET/POST rate-plans`, `PUT rate-plans/{id}`, `GET/POST room-rates`, `GET/POST tax-rules`, `PUT tax-rules/{id}`, `GET/POST service-items`, `PUT service-items/{id}`, `POST quotes` | `rates.manage` (read: `rooms.view`) |
| Guests | `GET guests?search`, `GET guests/{id}`, `POST guests`, `PUT guests/{id}` | `guests.view`, `guests.manage` |
| Reservations | `GET availability?arrivalDate&departureDate&adults`, `GET reservations`, `GET/PUT reservations/{id}`, `POST reservations`, `POST reservations/{id}/cancel`, `/no-show`, `/assign-room`, `/unassign-room` | `reservations.view`, `reservations.manage`; overbooking needs `reservations.overbook`, discounts need `discounts.approve` |
| Front desk | `GET front-desk/summary`, `/arrivals`, `/in-house`, `/departures`, `GET stays/{id}`, `POST front-desk/check-in`, `POST front-desk/check-out` | `frontdesk.checkin`, `frontdesk.checkout`; leaving with a balance needs `frontdesk.checkout.balance-override` and a reason |
| Housekeeping | `GET/POST housekeeping/tasks`, `POST housekeeping/tasks/{id}/assign`, `/start`, `/complete`, `/inspect`, `/cancel` | `housekeeping.view`, `housekeeping.work`, `housekeeping.inspect` |
| Folios | `GET folios/{id}`, `GET reservations/{id}/folio`, `POST folios/{id}/charges`, `POST folios/{id}/charges/{chargeId}/reverse`, `GET/POST folios/{id}/invoices` | `folios.view`, `folios.post`, `folios.adjust` |
| Payments | `POST folios/{id}/payments`, `POST payments/{id}/verify`, `GET receipts/{id}` | `payments.record`, `payments.verify` |
| Refunds | `GET refunds`, `POST folios/{id}/refunds`, `POST refunds/{id}/decision`, `POST refunds/{id}/execute` | `refunds.request`, `refunds.approve` (the approver must not be the requester) |
| Cashier | `POST cashier/shifts`, `GET cashier/shifts/current`, `GET cashier/shifts`, `GET cashier/shifts/{id}`, `POST cashier/shifts/{id}/close`, `POST cashier/shifts/{id}/approve-variance` | `cashier.operate`, `cashier.variance.approve` |

Common business error codes: `ROOM_NOT_AVAILABLE`, `ROOM_NOT_READY`, `ROOM_UNAVAILABLE`,
`BALANCE_DUE`, `SHIFT_REQUIRED`, `STAY_CLOSED`, `FOLIO_CLOSED`, `IDEMPOTENCY_KEY_REQUIRED`.
