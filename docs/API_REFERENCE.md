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
| POST | `/system/jobs/run` | `system.manage` — send due notifications and scan uploaded files now |
| GET | `/analytics/branches?from&to` | `reports.view` — compares every branch the caller can access |

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

## Operations and finance (phase 3)

Same rules as phase 2: everything sits under `/branches/{branchId}`, updates send `concurrencyStamp`,
and stock, receipt, part-issue, settlement and supplier-payment POSTs take an `Idempotency-Key` header.

| Area | Routes | Permissions |
| --- | --- | --- |
| Inventory | `GET/POST inventory/suppliers`, `PUT inventory/suppliers/{id}`, `GET/POST inventory/locations`, `PUT inventory/locations/{id}`, `GET/POST inventory/items`, `PUT inventory/items/{id}`, `GET inventory/balances`, `GET inventory/movements`, `POST inventory/receipts`, `/counts`, `/transfers`, `/wastage` | `inventory.view`, `inventory.manage`; suppliers need `purchasing.manage` |
| Purchasing | `GET/POST purchasing/requisitions`, `POST purchasing/requisitions/{id}/decision`, `GET/POST purchasing/orders`, `GET purchasing/orders/{id}`, `POST purchasing/orders/{id}/approve`, `/cancel`, `/close`, `GET/POST purchasing/orders/{id}/receipts`, `GET purchasing/uninvoiced-receipts`, `GET/POST purchasing/invoices`, `POST purchasing/invoices/{id}/approve`, `POST purchasing/invoices/{id}/payments` | `purchasing.manage`, `purchasing.approve` (never the person who raised it), `inventory.manage` to receive, `accounting.post` to pay |
| Restaurant | `GET/POST restaurant/outlets`, `PUT restaurant/outlets/{id}`, `.../tables`, `.../categories`, `.../menu`, `GET/POST restaurant/orders`, `GET restaurant/orders/{id}`, `POST restaurant/orders/{id}/lines`, `/lines/{lineId}/void`, `/send`, `/void`, `/charge-to-room`, `/settle`, `GET restaurant/kitchen/tickets`, `POST restaurant/kitchen/tickets/{id}/status` | `restaurant.operate`, `restaurant.manage` (set-up, and voiding food already sent); charge-to-room also needs `folios.post`, settle also needs `payments.record` |
| Maintenance | `GET/POST maintenance/assets`, `PUT maintenance/assets/{id}`, `GET maintenance/assets/{id}/history`, `GET/POST maintenance/requests`, `POST maintenance/requests/{id}/triage`, `GET/POST maintenance/work-orders`, `GET maintenance/work-orders/{id}`, `POST .../assign`, `/start`, `/parts`, `/finish`, `/inspect`, `/cancel`, `GET/POST maintenance/preventive-plans`, `PUT maintenance/preventive-plans/{id}`, `POST maintenance/preventive-plans/generate` | `maintenance.view` (anyone can report), `maintenance.work`, `maintenance.manage`; the assigned technician cannot inspect their own work |
| Staff | `GET/POST staff/departments`, `GET/POST staff/employees`, `GET/POST staff/shifts` (+ `PUT`), `GET/POST staff/roster`, `DELETE staff/roster/{id}`, `POST staff/attendance/clock-in`, `/clock-out`, `GET staff/attendance`, `GET staff/attendance/summary`, `GET/POST staff/leave`, `POST staff/leave/{id}/decision`, `GET/POST staff/overtime`, `POST staff/overtime/{id}/decision`, `GET/POST staff/handovers`, `POST staff/handovers/{id}/acknowledge` | `staff.view`, `staff.manage`; anyone can clock themselves in and write handovers |
| Accounting | `GET/POST accounting/accounts`, `PUT accounting/accounts/{id}`, `GET accounting/accounts/{id}/ledger`, `GET/PUT accounting/mappings`, `GET accounting/periods`, `POST accounting/periods/{id}/close`, `/reopen`, `GET/POST accounting/journals`, `GET accounting/journals/{id}`, `POST accounting/journals/{id}/reverse`, `GET accounting/trial-balance?from&to&consolidated` | `accounting.view`, `accounting.post`, `accounting.manage` |

Phase 3 error codes: `INSUFFICIENT_STOCK`, `OVER_RECEIPT`, `PO_STATE`, `INVOICE_STATE`, `DUPLICATE_SUPPLIER_INVOICE`,
`OVERPAYMENT`, `SELF_APPROVAL`, `TABLE_OCCUPIED`, `AMOUNT_MISMATCH`, `NOTHING_TO_SEND`, `WORK_ORDER_STATE`,
`ROSTER_OVERLAP`, `REST_PERIOD`, `ROSTERED_DURING_LEAVE`, `ALREADY_CLOCKED_IN`, `NO_EMPLOYEE_RECORD`,
`JOURNAL_UNBALANCED` (400), `CONTROL_ACCOUNT`, `PERIOD_CLOSED`, `PERIOD_NOT_ENDED`, `JOURNAL_ALREADY_REVERSED`.

## Integration (phase 4)

Branch-scoped as before. Audit search and export sit on the branch routes.

| Area | Routes | Permissions |
| --- | --- | --- |
| Night audit | `GET business-day`, `GET business-day/closes?from&to`, `POST business-day/close` (body: the date to close), `POST business-day/exceptions/{id}/accept` (reason required) | read `reports.view`; close and accept `businessday.close` |
| Analytics | `GET analytics/dashboard?from&to`, `POST analytics/refresh`, `GET analytics/forecast?days` | `reports.view` |
| Notifications | `GET notifications/outbox?status&eventType`, `POST notifications/outbox/{id}/retry`, `/cancel`, `GET/POST notifications/templates`, `GET notifications/inbox?unreadOnly`, `POST notifications/inbox/{id}/read`, `GET/PUT notifications/preferences` | outbox and templates `notifications.manage`; inbox and preferences any branch user |
| Documents | `GET/POST documents` (multipart: `file`, `title`, `category`, `classification`, `entityType`, `entityId`, `retentionUntil`), `GET documents/{id}`, `POST documents/{id}/versions`, `GET documents/{id}/content?versionId`, `POST documents/{id}/versions/{vid}/decision`, `/rescan`, `/signatures`, `GET documents/{id}/signatures`, `PUT documents/{id}/retention`, `DELETE documents/{id}` | `documents.view`; uploads need `documents.manage` (guest ID also allowed with `guests.manage`); approval `documents.approve`; Confidential needs manage or approve to read, Restricted needs approve |
| Security log | `GET/POST security/incidents`, `GET security/incidents/{id}`, `POST security/incidents/{id}/actions` (assign, note, resolve, close), `GET/POST security/lost-found`, `POST security/lost-found/{id}/custody` (move, note, return, dispose, donate), `GET/POST security/visitors`, `POST security/visitors/{id}/sign-out`, `POST security/visitors/purge`, `GET/POST security/emergencies`, `POST security/emergencies/{id}/actions`, `/end` | `security.view` to report; `security.manage` to move cases on, release property, purge visitors and end emergencies |
| Complaints | `GET/POST complaints`, `POST complaints/{id}/actions` (assign, resolve, close) | `guests.view`, `guests.manage` |
| Audit | `GET /branches/{id}/audit-logs?entityType&entityId&userId&action&fromUtc&toUtc`, `GET /branches/{id}/audit-logs/export` (CSV, max 50,000 rows) | `audit.view` |

How the night audit works: days close in order. `NextDateToClose` is the day after the last closed day
(or today for a branch that has never closed), and it cannot be closed before it starts. Each run re-checks
the same record. Blocking findings (`ARRIVAL_PENDING`, `DEPARTURE_PENDING`, `OPEN_RESTAURANT_ORDER`,
`AR_LEDGER_MISMATCH`) must be fixed or accepted with a reason; warnings (`CASHIER_SHIFT_OPEN`,
`PAYMENTS_UNVERIFIED`, `REFUNDS_NOT_PAID`, `INVENTORY_LEDGER_MISMATCH`) are recorded only. A successful close
takes the final daily snapshot, moves the branch to the next business date, and sends the daily report to
everyone with `reports.view`. Sending the same close again returns the original result.

Phase 4 error codes: `BUSINESS_DATE_MISMATCH`, `BUSINESS_DAY_NOT_STARTED`, `BUSINESS_DAY_BLOCKED`, `BUSINESS_DAY_CLOSED`,
`EXCEPTION_STATE`, `REASON_REQUIRED` (400), `OUTBOX_STATE`, `DOCUMENT_NOT_RELEASED`, `DOCUMENT_QUARANTINED`,
`DOCUMENT_NOT_APPROVED`, `DOCUMENT_FILE_MISSING`, `DOCUMENT_DELETED`, `SCAN_PENDING`, `SCAN_STATE`, `SCAN_DONE`,
`APPROVAL_STATE`, `SELF_APPROVAL`, `LEGAL_HOLD`, `RETENTION_ACTIVE`, `CASE_STATE`, `CASE_CLOSED`, `HOLD_PERIOD`,
`ITEM_RELEASED`, `VISITOR_LEFT`, `EMERGENCY_ENDED`. Two requests racing on a unique index now return 409
`CONCURRENCY_CONFLICT` instead of 500.

## Public booking (phase 5)

Anonymous routes for the booking website under `/api/v1/public`. They are rate limited per client address
(`RateLimiting:PublicReadPerMinute`, default 60; `PublicWritePerMinute`, default 10) and allow cross-origin calls only
from `PublicBooking:AllowedOrigins`. Only branches with public booking turned on are visible.

| Method | Route | Notes |
| --- | --- | --- |
| GET | `/public/branches` | bookable branches: code, name, currency |
| GET | `/public/branches/{code}/availability?arrival&departure&adults&children` | room types with a price quote; availability is capped at 5 so exact stock is not revealed |
| POST | `/public/branches/{code}/holds` | dates and up to 4 rooms → hold with `reference` and a one-time `accessToken`; rooms are held for `PublicBooking:HoldMinutes` (15) |
| GET | `/public/holds/{reference}` | needs `X-Booking-Token` |
| PUT | `/public/holds/{reference}/guest` | guest name, email, phone, requests; a filled `website` field (honeypot) is silently ignored |
| DELETE | `/public/holds/{reference}` | releases the rooms |
| POST | `/public/holds/{reference}/payments` | starts a Paystack payment and returns the checkout URL. The hold is extended once while payment is under way |
| POST | `/public/payments/{merchantReference}/verify` | asks Paystack for the result; confirms the booking when amount, currency and reference all match |
| POST | `/public/payments/paystack/webhook` | Paystack server-to-server event; signature checked with HMAC-SHA512, body limit 64 KB, no CORS |
| GET | `/public/bookings/{reference}` | confirmed booking with its reservations; needs `X-Booking-Token` |
| POST | `/public/bookings/{reference}/cancellation-request` | records the request and notifies staff; staff cancel through the normal reservation routes |

A wrong token and an unknown reference both return 404. A payment that arrives after the hold expired books the
rooms again if they are still free; otherwise it is marked for review and staff are notified.

Staff routes:

| Method | Route | Permission |
| --- | --- | --- |
| PUT | `/branches/{branchId}/public-booking` | `branches.manage`; `{ enabled }` |
| GET | `/branches/{branchId}/online-payments?status` | `payments.verify` |
| POST | `/branches/{branchId}/online-payments/{paymentId}/resolve` | `refunds.approve`; a note is required |
| GET | `/system/restore-check?checksums=true` | `system.manage`; see [Backup and recovery](BACKUP_AND_RECOVERY.md) |

Phase 5 error codes: `TOO_MANY_HOLDS` (429), `TOO_MANY_ROOMS`, `QUANTITY_INVALID`, `REQUESTS_TOO_LONG`,
`FIRST_NAME_REQUIRED`, `LAST_NAME_REQUIRED`, `EMAIL_REQUIRED`, `NOTE_REQUIRED` (400), `HOLD_EXPIRED`, `HOLD_NOT_ACTIVE`,
`GUEST_DETAILS_REQUIRED`, `PAYMENT_IN_PROGRESS`, `PAYMENT_STATE`, `BOOKING_NOT_CONFIRMED`, `ROOM_NOT_AVAILABLE`,
`HOLD_INVENTORY_MISSING` (409), `PAYMENT_UNAVAILABLE` (503), `ROLE_GRANT_FORBIDDEN` (403, granting access you do not hold).
