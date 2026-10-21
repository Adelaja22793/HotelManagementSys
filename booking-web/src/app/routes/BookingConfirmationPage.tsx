import { useEffect, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { BookingDetails } from "../../components/BookingDetails";
import { ErrorBanner } from "../../components/ErrorBanner";
import { Steps } from "../../components/Steps";
import { api } from "../../services/api";
import { tokens } from "../../services/tokens";
import type { Booking } from "../../types/api";

const MaxChecks = 6;
const CheckEveryMs = 3000;

/** Where the payment provider sends the guest back. The API asks the provider itself; the query string is not trusted. */
export function BookingConfirmationPage() {
  const { reference = "" } = useParams();
  const [params] = useSearchParams();
  const merchantReference = params.get("ref") ?? params.get("reference") ?? params.get("trxref") ?? "";
  const [booking, setBooking] = useState<Booking | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [checks, setChecks] = useState(0);

  useEffect(() => {
    let cancelled = false;
    let timer: number | undefined;
    async function check(attempt: number) {
      try {
        const result = await api.verifyPayment(reference, merchantReference);
        if (cancelled) return;
        setBooking(result);
        setChecks(attempt);
        if (result.status === "Active" && result.paymentStatus === "Initialized" && attempt < MaxChecks)
          timer = window.setTimeout(() => void check(attempt + 1), CheckEveryMs);
      } catch (e) {
        if (!cancelled) setError(e);
      }
    }
    if (merchantReference) void check(1);
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [reference, merchantReference]);

  if (!merchantReference) return <ErrorBanner error={new Error("missing reference")} />;
  if (error) return <ErrorBanner error={error} />;
  if (!booking) return <p>Confirming your payment…</p>;

  const confirmed = booking.status === "Converted";
  const manage = tokens.manageLink(reference);
  return (
    <>
      <Steps current={confirmed ? 4 : 3} />
      <h1>{confirmed ? "Booking confirmed" : booking.status === "NeedsReview" ? "We are checking your payment" : "Payment not completed"}</h1>
      {booking.message && <div className={confirmed ? "banner banner-ok" : "banner"}>{booking.message}</div>}
      {booking.status === "Active" && booking.paymentStatus === "Initialized" && checks >= MaxChecks && (
        <p>Your bank has not confirmed the payment yet. We will email you as soon as it does; you do not need to pay again.</p>
      )}
      {booking.status === "Active" && booking.paymentStatus === "Failed" && (
        <Link className="button primary" to={`/booking/${reference}/pay`}>Try paying again</Link>
      )}
      <BookingDetails booking={booking} />
      {confirmed && manage && (
        <div className="card">
          <p>Keep this private link to view or change your booking later:</p>
          <input className="copy" readOnly value={manage} onFocus={(e) => e.currentTarget.select()} aria-label="Manage booking link" />
        </div>
      )}
    </>
  );
}
