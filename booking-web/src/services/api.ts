import type { ApiErrorBody, Availability, Booking, GuestDetails, Hold, PaymentInit, PublicBranch } from "../types/api";
import { tokens } from "./tokens";

const base = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/$/, "") + "/api/v1/public";

/** An error the API returned in its standard shape, or a network failure. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
    public readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

async function request<T>(path: string, init: RequestInit & { reference?: string } = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body !== undefined) headers.set("Content-Type", "application/json");
  const token = init.reference ? tokens.get(init.reference) : null;
  if (token) headers.set("X-Booking-Token", token);

  let response: Response;
  try {
    response = await fetch(base + path, { ...init, headers, credentials: "omit" });
  } catch {
    throw new ApiError(0, "NETWORK", "We could not reach the hotel. Check your connection and try again.");
  }
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  const body = text ? JSON.parse(text) : undefined;
  if (!response.ok) {
    const error = body as ApiErrorBody | undefined;
    if (response.status === 429) throw new ApiError(429, "RATE_LIMITED", "Too many attempts. Please wait a minute and try again.");
    throw new ApiError(response.status, error?.code ?? "ERROR", error?.description ?? "Something went wrong. Please try again.", error?.errors);
  }
  return body as T;
}

const json = (value: unknown) => JSON.stringify(value);
const enc = encodeURIComponent;

export const api = {
  branches: () => request<PublicBranch[]>("/branches"),

  availability: (branchCode: string, arrival: string, departure: string, adults: number, children: number) =>
    request<Availability>(
      `/branches/${enc(branchCode)}/availability?arrival=${arrival}&departure=${departure}&adults=${adults}&children=${children}`,
    ),

  createHold: async (branchCode: string, arrival: string, departure: string, adults: number, children: number, rooms: { roomTypeId: string; quantity: number }[]) => {
    const hold = await request<Hold>(`/branches/${enc(branchCode)}/holds`, {
      method: "POST",
      body: json({ arrivalDate: arrival, departureDate: departure, adults, children, rooms }),
    });
    if (hold.accessToken) tokens.set(hold.reference, hold.accessToken);
    return hold;
  },

  hold: (reference: string) => request<Hold>(`/holds/${enc(reference)}`, { reference }),

  setGuest: (reference: string, details: GuestDetails) =>
    request<Hold>(`/holds/${enc(reference)}/guest`, { method: "PUT", body: json(details), reference }),

  releaseHold: (reference: string) => request<void>(`/holds/${enc(reference)}`, { method: "DELETE", reference }),

  startPayment: (reference: string) => request<PaymentInit>(`/holds/${enc(reference)}/payments`, { method: "POST", reference }),

  verifyPayment: (reference: string, merchantReference: string) =>
    request<Booking>(`/payments/${enc(merchantReference)}/verify`, { method: "POST", reference }),

  booking: (reference: string) => request<Booking>(`/bookings/${enc(reference)}`, { reference }),

  requestCancellation: (reference: string, reason: string) =>
    request<Booking>(`/bookings/${enc(reference)}/cancellation-request`, { method: "POST", body: json({ reason }), reference }),
};
