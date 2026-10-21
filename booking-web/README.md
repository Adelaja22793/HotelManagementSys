# Booking website

The public booking site guests use to search rooms, hold them, pay with Paystack and manage their booking.
It is a static React app (Vite, TypeScript) that talks only to the `/api/v1/public` routes of the HMS API.

## Run it locally

1. Start the API in Development (`dotnet run --project src/HMS.Api`). Development uses the sandbox payment provider,
   so no Paystack keys are needed.
2. Turn on public booking for a branch: `PUT /api/v1/branches/{id}/public-booking` with `{ "enabled": true }`.
3. In this folder run `npm install` and then `npm run dev`, and open http://localhost:5173.
   The dev server forwards `/api` to the API at https://localhost:7180.

## Commands

| Command | What it does |
| --- | --- |
| `npm run dev` | Dev server with hot reload |
| `npm test` | Unit and flow tests (Vitest and Testing Library) |
| `npm run build` | Typecheck and build static files into `dist/` |

## Configuration

`VITE_API_BASE_URL` (see `.env.example`) is the API origin when the site and the API are on different hosts.
Leave it empty when a reverse proxy serves both from one origin. The site's origin must be listed in the API's
`PublicBooking:AllowedOrigins`, and `PublicBooking:SiteUrl` must point at the site so Paystack returns guests to it.

## How it keeps a booking private

Creating a hold returns a one-time access token. The site keeps it in `localStorage` for that booking only and sends it
in the `X-Booking-Token` header. The manage link puts the token after `#t=`, which browsers never send to a server;
the page moves it into storage and removes it from the address bar. Requests never send cookies.

See `docs/DEPLOYMENT.md` for hosting and the security headers to set.
