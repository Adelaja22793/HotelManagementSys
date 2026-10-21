import { describe, expect, it } from "vitest";
import { validateGuest, validateSearch } from "../validation/guest";

const good = { firstName: "Ada", lastName: "Okafor", email: "ada@example.com", phone: "+234 801 234 5678", country: "NG",
  specialRequests: "", acceptTerms: true, website: "" };

describe("guest details", () => {
  it("accepts complete details", () => expect(validateGuest(good)).toEqual({}));
  it("requires names, a real email and the terms", () => {
    const errors = validateGuest({ ...good, firstName: " ", lastName: "", email: "ada@", acceptTerms: false });
    expect(Object.keys(errors).sort()).toEqual(["acceptTerms", "email", "firstName", "lastName"]);
  });
  it("checks optional phone numbers only when given", () => {
    expect(validateGuest({ ...good, phone: "" }).phone).toBeUndefined();
    expect(validateGuest({ ...good, phone: "call me" }).phone).toBeDefined();
  });
});

describe("search", () => {
  const today = "2026-10-21";
  const ok = { branchCode: "LOS", arrival: "2026-10-22", departure: "2026-10-24", adults: 2, children: 0 };
  it("accepts a future stay", () => expect(validateSearch(ok, today)).toBeNull());
  it("refuses past arrivals and backwards dates", () => {
    expect(validateSearch({ ...ok, arrival: "2026-10-20" }, today)).toMatch(/past/);
    expect(validateSearch({ ...ok, departure: "2026-10-22" }, today)).toMatch(/after arrival/);
  });
  it("needs a hotel and at least one adult", () => {
    expect(validateSearch({ ...ok, branchCode: "" }, today)).toMatch(/hotel/);
    expect(validateSearch({ ...ok, adults: 0 }, today)).toMatch(/adults/);
  });
});
