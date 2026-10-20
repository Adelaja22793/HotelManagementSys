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
