import { useState } from "react";
import { Navigate, useNavigate, useParams } from "react-router-dom";
import { Countdown } from "../../components/Countdown";
import { ErrorBanner } from "../../components/ErrorBanner";
import { StaySummary } from "../../components/StaySummary";
import { Steps } from "../../components/Steps";
import { useAsync } from "../../components/useAsync";
import { api } from "../../services/api";
import { money } from "../../services/format";
import { HoldGone } from "./HoldGone";

export function PaymentPage() {
  const { reference = "" } = useParams();
  const navigate = useNavigate();
  const hold = useAsync(() => api.hold(reference), [reference]);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [expired, setExpired] = useState(false);

  async function pay() {
    setBusy(true);
    setError(null);
    try {
      const payment = await api.startPayment(reference);
      // Leave for the provider's secure page; it sends the guest back to the confirmation page.
      window.location.assign(payment.authorizationUrl);
    } catch (e) {
      setError(e);
      setBusy(false);
    }
  }

  async function cancel() {
    setBusy(true);
    try {
      await api.releaseHold(reference);
      navigate("/");
    } catch (e) {
      setError(e);
      setBusy(false);
    }
  }

  if (hold.error) return <ErrorBanner error={hold.error} />;
  if (!hold.data) return <p>Loading your booking…</p>;
  if (expired || hold.data.status !== "Active") return <HoldGone hold={hold.data} />;
  if (!hold.data.hasGuestDetails) return <Navigate to={`/booking/${reference}/guest`} replace />;

  return (
    <>
      <Steps current={3} />
      <h1>Payment</h1>
      <Countdown expiresAtUtc={hold.data.expiresAtUtc} onExpired={() => setExpired(true)} />
      <div className="two-column">
        <div className="card">
          <ErrorBanner error={error} />
          <p>
            Booking for <strong>{hold.data.guestName}</strong> ({hold.data.guestEmail}).
          </p>
          <p className="total">Amount to pay now: {money(hold.data.amountDueNow, hold.data.currency)}</p>
          <p className="muted">
            You will be taken to Paystack to pay by card, bank transfer or USSD. Your booking is confirmed as soon as the payment is verified.
          </p>
          <button className="primary" onClick={pay} disabled={busy}>
            {busy ? "Opening secure payment…" : "Pay securely with Paystack"}
          </button>
          <button className="link" onClick={cancel} disabled={busy}>
            Cancel and release the rooms
          </button>
        </div>
        <StaySummary hold={hold.data} />
      </div>
    </>
  );
}
