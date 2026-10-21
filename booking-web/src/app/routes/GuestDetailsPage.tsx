import { useState, type FormEvent, type ReactNode } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { Countdown } from "../../components/Countdown";
import { ErrorBanner } from "../../components/ErrorBanner";
import { StaySummary } from "../../components/StaySummary";
import { Steps } from "../../components/Steps";
import { useAsync } from "../../components/useAsync";
import { api } from "../../services/api";
import type { GuestDetails } from "../../types/api";
import { validateGuest, type GuestErrors } from "../../validation/guest";
import { HoldGone } from "./HoldGone";

const empty: GuestDetails = { firstName: "", lastName: "", email: "", phone: "", country: "", specialRequests: "", acceptTerms: false, website: "" };

export function GuestDetailsPage() {
  const { reference = "" } = useParams();
  const navigate = useNavigate();
  const hold = useAsync(() => api.hold(reference), [reference]);
  const [details, setDetails] = useState<GuestDetails>(empty);
  const [errors, setErrors] = useState<GuestErrors>({});
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [expired, setExpired] = useState(false);

  const set = <K extends keyof GuestDetails>(field: K, value: GuestDetails[K]) => setDetails({ ...details, [field]: value });

  async function submit(event: FormEvent) {
    event.preventDefault();
    const found = validateGuest(details);
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    setBusy(true);
    setError(null);
    try {
      await api.setGuest(reference, details);
      navigate(`/booking/${reference}/pay`);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  if (hold.error) return <ErrorBanner error={hold.error} />;
  if (!hold.data) return <p>Loading your booking…</p>;
  if (expired || hold.data.status !== "Active") return <HoldGone hold={hold.data} />;

  return (
    <>
      <Steps current={2} />
      <h1>Your details</h1>
      <Countdown expiresAtUtc={hold.data.expiresAtUtc} onExpired={() => setExpired(true)} />
      <div className="two-column">
        <form className="card form" onSubmit={submit} noValidate>
          <ErrorBanner error={error} />
          <div className="row">
            <Field label="First name" error={errors.firstName}>
              <input autoComplete="given-name" value={details.firstName} onChange={(e) => set("firstName", e.target.value)} maxLength={100} />
            </Field>
            <Field label="Last name" error={errors.lastName}>
              <input autoComplete="family-name" value={details.lastName} onChange={(e) => set("lastName", e.target.value)} maxLength={100} />
            </Field>
          </div>
          <Field label="Email (for your confirmation and receipt)" error={errors.email}>
            <input type="email" autoComplete="email" value={details.email} onChange={(e) => set("email", e.target.value)} maxLength={254} />
          </Field>
          <div className="row">
            <Field label="Phone (optional)" error={errors.phone}>
              <input type="tel" autoComplete="tel" value={details.phone} onChange={(e) => set("phone", e.target.value)} maxLength={20} />
            </Field>
            <Field label="Country (optional)">
              <input autoComplete="country-name" value={details.country} onChange={(e) => set("country", e.target.value)} maxLength={60} />
            </Field>
          </div>
          <Field label="Special requests (optional)" error={errors.specialRequests}>
            <textarea rows={3} value={details.specialRequests} onChange={(e) => set("specialRequests", e.target.value)} maxLength={500} />
          </Field>
          {/* Honeypot: hidden from people and screen readers; bots fill it in. */}
          <div className="hp" aria-hidden="true">
            <label>
              Website
              <input tabIndex={-1} autoComplete="off" value={details.website} onChange={(e) => set("website", e.target.value)} />
            </label>
          </div>
          <label className="checkbox">
            <input type="checkbox" checked={details.acceptTerms} onChange={(e) => set("acceptTerms", e.target.checked)} />
            I accept the booking terms and the cancellation policy shown for my rooms.
          </label>
          {errors.acceptTerms && <p className="field-error">{errors.acceptTerms}</p>}
          <button type="submit" className="primary" disabled={busy}>
            {busy ? "Saving…" : "Continue to payment"}
          </button>
        </form>
        <StaySummary hold={hold.data} />
      </div>
    </>
  );
}

function Field({ label, error, children }: { label: string; error?: string; children: ReactNode }) {
  return (
    <label className={error ? "field field-invalid" : "field"}>
      {label}
      {children}
      {error && <span className="field-error">{error}</span>}
    </label>
  );
}
