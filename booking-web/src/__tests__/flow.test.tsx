import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppRoutes } from "../app/App";
import { tokens } from "../services/tokens";
import type { Availability, Booking, Hold } from "../types/api";

const offer: Availability = {
  branchCode: "LOS", arrivalDate: "2030-01-10", departureDate: "2030-01-12", nights: 2, adults: 2, children: 0,
  offers: [{
    roomTypeId: "rt-1", code: "STD", name: "Standard Room", description: "Queen bed", maxAdults: 2, maxChildren: 1, available: 2,
    ratePlanName: "Best Available Rate", isRefundable: true, cancellationPolicy: "Free cancellation until 24h before arrival.",
    currency: "NGN", nights: [{ date: "2030-01-10", rate: 50000 }, { date: "2030-01-11", rate: 50000 }],
    roomSubtotal: 100000, taxAmount: 5000, totalAmount: 105000, amountDueNow: 105000,
  }],
};

const hold = (overrides: Partial<Hold> = {}): Hold => ({
  reference: "W7K3P9QX2M", accessToken: null, branchCode: "LOS", branchName: "Lagos Main", arrivalDate: "2030-01-10", departureDate: "2030-01-12",
  adults: 2, children: 0, status: "Active", expiresAtUtc: new Date(Date.now() + 15 * 60_000).toISOString(), currency: "NGN",
  totalAmount: 105000, amountDueNow: 105000,
  rooms: [{ roomTypeId: "rt-1", roomTypeName: "Standard Room", quantity: 1, ratePlanName: "Best Available Rate", isRefundable: true,
    cancellationPolicy: "Free cancellation until 24h before arrival.", totalPerRoom: 105000, depositPerRoom: 105000 }],
  hasGuestDetails: false, guestName: null, guestEmail: null, ...overrides,
});

const confirmed: Booking = {
  reference: "W7K3P9QX2M", status: "Converted", paymentStatus: "Succeeded", branchName: "Lagos Main", branchPhone: "+234 1 000 0000",
  arrivalDate: "2030-01-10", departureDate: "2030-01-12", guestName: "Ada Okafor", currency: "NGN", totalAmount: 105000, amountPaid: 105000,
  reservations: [{ reservationNumber: "LOS-RES-000123", roomTypeName: "Standard Room", status: "Confirmed", totalAmount: 105000,
    cancellationPolicy: "Free cancellation until 24h before arrival." }],
  cancellationRequested: false, message: "Your booking is confirmed. A confirmation email is on its way.",
};

type Call = { url: string; method: string; body: unknown; token: string | null };
let calls: Call[] = [];

function mockApi(routes: Record<string, (call: Call) => unknown>) {
  vi.stubGlobal("fetch", vi.fn(async (url: string, init: RequestInit = {}) => {
    const headers = new Headers(init.headers);
    const call: Call = { url, method: init.method ?? "GET", body: init.body ? JSON.parse(String(init.body)) : undefined, token: headers.get("X-Booking-Token") };
    calls.push(call);
    const key = Object.keys(routes).find((k) => `${call.method} ${url}`.includes(k));
    if (!key) return new Response(JSON.stringify({ code: "NOT_FOUND", description: "Not found" }), { status: 404 });
    const result = routes[key]!(call);
    return result instanceof Response ? result : new Response(JSON.stringify(result), { status: 200 });
  }));
}

beforeEach(() => {
  calls = [];
});
afterEach(() => vi.unstubAllGlobals());

const renderAt = (path: string) => render(<MemoryRouter initialEntries={[path]}><AppRoutes /></MemoryRouter>);

describe("booking flow", () => {
  it("holds the chosen room, stores the token and moves to guest details", async () => {
    mockApi({
      "GET /api/v1/public/branches/LOS/availability": () => offer,
      "POST /api/v1/public/branches/LOS/holds": () => new Response(JSON.stringify(hold({ accessToken: "secret-token-abcdefghijklmnop" })), { status: 201 }),
      "GET /api/v1/public/holds/W7K3P9QX2M": () => hold(),
    });
    renderAt("/availability?branch=LOS&arrival=2030-01-10&departure=2030-01-12&adults=2&children=0");

    await screen.findByText("Standard Room");
    await userEvent.click(screen.getByRole("button", { name: "Add a Standard Room" }));
    await userEvent.click(screen.getByRole("button", { name: "Reserve" }));

    await screen.findByRole("heading", { name: "Your details" });
    const holdCall = calls.find((c) => c.method === "POST")!;
    expect(holdCall.body).toEqual({ arrivalDate: "2030-01-10", departureDate: "2030-01-12", adults: 2, children: 0, rooms: [{ roomTypeId: "rt-1", quantity: 1 }] });
    expect(tokens.get("W7K3P9QX2M")).toBe("secret-token-abcdefghijklmnop");
    // Later calls prove ownership with the token header.
    expect(calls.at(-1)!.token).toBe("secret-token-abcdefghijklmnop");
  });

  it("checks guest details before sending and never sends the honeypot as filled", async () => {
    tokens.set("W7K3P9QX2M", "secret-token-abcdefghijklmnop");
    mockApi({
      "GET /api/v1/public/holds/W7K3P9QX2M": () => hold(),
      "PUT /api/v1/public/holds/W7K3P9QX2M/guest": () => hold({ hasGuestDetails: true, guestName: "Ada Okafor", guestEmail: "ada@example.com" }),
    });
    renderAt("/booking/W7K3P9QX2M/guest");
    await screen.findByRole("heading", { name: "Your details" });

    await userEvent.click(screen.getByRole("button", { name: "Continue to payment" }));
    expect(screen.getByText("Enter your first name.")).toBeInTheDocument();
    expect(calls.some((c) => c.method === "PUT")).toBe(false);

    await userEvent.type(screen.getByLabelText(/^First name/), "Ada");
    await userEvent.type(screen.getByLabelText(/^Last name/), "Okafor");
    await userEvent.type(screen.getByLabelText(/^Email/), "ada@example.com");
    await userEvent.click(screen.getByRole("checkbox"));
    await userEvent.click(screen.getByRole("button", { name: "Continue to payment" }));

    await waitFor(() => expect(calls.some((c) => c.method === "PUT")).toBe(true));
    const sent = calls.find((c) => c.method === "PUT")!.body as Record<string, unknown>;
    expect(sent).toMatchObject({ firstName: "Ada", lastName: "Okafor", email: "ada@example.com", acceptTerms: true, website: "" });
  });

  it("shows an expired hold instead of the form", async () => {
    tokens.set("W7K3P9QX2M", "secret-token-abcdefghijklmnop");
    mockApi({ "GET /api/v1/public/holds/W7K3P9QX2M": () => hold({ status: "Expired" }) });
    renderAt("/booking/W7K3P9QX2M/guest");
    expect(await screen.findByText(/hold has expired/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Search again" })).toHaveAttribute("href", expect.stringContaining("branch=LOS"));
  });

  it("confirms the payment with the API after the provider sends the guest back", async () => {
    tokens.set("W7K3P9QX2M", "secret-token-abcdefghijklmnop");
    mockApi({ "POST /api/v1/public/payments/HMS-W7K3P9QX2M-ABC123/verify": () => confirmed });
    renderAt("/booking/W7K3P9QX2M/payment-return?ref=HMS-W7K3P9QX2M-ABC123&trxref=HMS-W7K3P9QX2M-ABC123");

    expect(await screen.findByRole("heading", { name: "Booking confirmed" })).toBeInTheDocument();
    expect(screen.getByText("LOS-RES-000123")).toBeInTheDocument();
    expect((screen.getByLabelText("Manage booking link") as HTMLInputElement).value).toContain("#t=secret-token-abcdefghijklmnop");
  });

  it("explains a booking it cannot find without leaking whether it exists", async () => {
    mockApi({});
    renderAt("/booking/NOPE123456");
    expect(await screen.findByRole("heading", { name: "Booking not found" })).toBeInTheDocument();
  });
});
