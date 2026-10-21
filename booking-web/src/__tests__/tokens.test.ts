import { describe, expect, it } from "vitest";
import { tokens } from "../services/tokens";

describe("booking tokens", () => {
  it("are stored per booking and case-insensitive on the reference", () => {
    tokens.set("abc123", "token-value-that-is-long-enough");
    expect(tokens.get("ABC123")).toBe("token-value-that-is-long-enough");
    expect(tokens.get("OTHER")).toBeNull();
  });

  it("are read from the manage link fragment", () => {
    tokens.adoptFromHash("REF1", "#t=AbCdEfGhIjKlMnOpQrStUvWxYz012345_-");
    expect(tokens.get("REF1")).toBe("AbCdEfGhIjKlMnOpQrStUvWxYz012345_-");
    expect(tokens.manageLink("REF1")).toBe(`${window.location.origin}/booking/REF1#t=AbCdEfGhIjKlMnOpQrStUvWxYz012345_-`);
  });

  it("ignore fragments that are not tokens", () => {
    tokens.adoptFromHash("REF2", "#t=<script>");
    expect(tokens.get("REF2")).toBeNull();
  });
});
