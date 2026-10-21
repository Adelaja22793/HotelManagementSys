import { useEffect, useState, type FormEvent } from "react";
import { useLocation, useParams } from "react-router-dom";
import { BookingDetails } from "../../components/BookingDetails";
import { ErrorBanner } from "../../components/ErrorBanner";
import { useAsync } from "../../components/useAsync";
import { api, ApiError } from "../../services/api";
import { tokens } from "../../services/tokens";

export function ManageBookingPage() {
  const { reference = "" } = useParams();
  const location = useLocation();
  // A saved link carries the token in the fragment; remember it before loading, then drop it from the address bar.
  if (location.hash) tokens.adoptFromHash(reference, location.hash);
  useEffect(() => {
    if (window.location.hash) window.history.replaceState(null, "", window.location.pathname);
  }, []);
  const booking = useAsync(() => api.booking(reference), [reference]);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  async function requestCancellation(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      booking.setData(await api.requestCancellation(reference, reason));
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  if (booking.error instanceof ApiError && booking.error.status === 404)
    return (
      <div className="card">
        <h1>Booking not found</h1>
        <p>Open the private link from your confirmation page or email on the device you booked with, or contact the hotel with your booking code.</p>
      </div>
    );
  if (booking.error) return <ErrorBanner error={booking.error} />;
  if (!booking.data) return <p>Loading your booking…</p>;

  const b = booking.data;
  return (
    <>
      <h1>Your booking {b.reference}</h1>
      {b.message && <div className="banner">{b.message}</div>}
      <BookingDetails booking={b} />
      {b.status === "Converted" && !b.cancellationRequested && (
        <form className="card form" onSubmit={requestCancellation}>
          <h2>Need to cancel?</h2>
          <p className="muted">The hotel will confirm the cancellation and any refund under your rate's cancellation policy.</p>
          <ErrorBanner error={error} />
          <label>
            Reason (optional)
            <textarea rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
          </label>
          <button type="submit" className="danger" disabled={busy}>
            {busy ? "Sending…" : "Request cancellation"}
          </button>
        </form>
      )}
    </>
  );
}
