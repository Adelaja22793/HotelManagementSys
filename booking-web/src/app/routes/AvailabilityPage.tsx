import { useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { ErrorBanner } from "../../components/ErrorBanner";
import { Steps } from "../../components/Steps";
import { useAsync } from "../../components/useAsync";
import { api } from "../../services/api";
import { longDate, money } from "../../services/format";

const MaxRooms = 4;

export function AvailabilityPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const branch = params.get("branch") ?? "";
  const arrival = params.get("arrival") ?? "";
  const departure = params.get("departure") ?? "";
  const adults = Number(params.get("adults") ?? 1);
  const children = Number(params.get("children") ?? 0);
  const result = useAsync(() => api.availability(branch, arrival, departure, adults, children), [branch, arrival, departure, adults, children]);
  const [quantities, setQuantities] = useState<Record<string, number>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const chosen = Object.entries(quantities).filter(([, q]) => q > 0).map(([roomTypeId, quantity]) => ({ roomTypeId, quantity }));
  const roomCount = chosen.reduce((sum, r) => sum + r.quantity, 0);
  const offers = result.data?.offers ?? [];
  const total = chosen.reduce((sum, r) => sum + (offers.find((o) => o.roomTypeId === r.roomTypeId)?.totalAmount ?? 0) * r.quantity, 0);
  const currency = offers[0]?.currency ?? "NGN";

  async function reserve() {
    setBusy(true);
    setError(null);
    try {
      const hold = await api.createHold(branch, arrival, departure, adults, children, chosen);
      navigate(`/booking/${hold.reference}/guest`);
    } catch (e) {
      setError(e);
      result.reload();
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <Steps current={1} />
      <h1>Choose your room</h1>
      <p>
        {arrival && longDate(arrival)} to {departure && longDate(departure)} · {adults} adult{adults === 1 ? "" : "s"}
        {children > 0 ? `, ${children} child${children === 1 ? "" : "ren"}` : ""} · <Link to={`/?${params}`}>Change</Link>
      </p>
      <ErrorBanner error={result.error ?? error} />
      {result.loading && <p>Checking availability…</p>}
      {!result.loading && !result.error && offers.length === 0 && (
        <div className="card">
          <p>Sorry, nothing is available for these dates. Try different dates or fewer guests.</p>
        </div>
      )}
      <div className="offers">
        {offers.map((offer) => {
          const quantity = quantities[offer.roomTypeId] ?? 0;
          const canAdd = quantity < offer.available && roomCount < MaxRooms;
          return (
            <article key={offer.roomTypeId} className="card offer">
              <h2>{offer.name}</h2>
              {offer.description && <p>{offer.description}</p>}
              <p className="muted">
                Sleeps {offer.maxAdults} adult{offer.maxAdults === 1 ? "" : "s"}
                {offer.maxChildren > 0 ? ` and ${offer.maxChildren} child${offer.maxChildren === 1 ? "" : "ren"}` : ""}
                {offer.available <= 3 ? ` · only ${offer.available} left` : ""}
              </p>
              <p>
                <strong>{money(offer.totalAmount, offer.currency)}</strong> for {offer.nights.length} night{offer.nights.length === 1 ? "" : "s"}
                <span className="muted"> (includes {money(offer.taxAmount, offer.currency)} tax)</span>
              </p>
              <p className="muted">
                {offer.ratePlanName} · {offer.isRefundable ? "Refundable" : "Non-refundable"} · {offer.cancellationPolicy}
              </p>
              <div className="quantity">
                <button type="button" aria-label={`Remove a ${offer.name}`} disabled={quantity === 0}
                  onClick={() => setQuantities({ ...quantities, [offer.roomTypeId]: quantity - 1 })}>−</button>
                <span aria-label={`${offer.name} rooms selected`}>{quantity}</span>
                <button type="button" aria-label={`Add a ${offer.name}`} disabled={!canAdd}
                  onClick={() => setQuantities({ ...quantities, [offer.roomTypeId]: quantity + 1 })}>+</button>
              </div>
            </article>
          );
        })}
      </div>
      {roomCount > 0 && (
        <div className="card sticky-total">
          <span>
            {roomCount} room{roomCount === 1 ? "" : "s"} · {money(total, currency)}
          </span>
          <button className="primary" onClick={reserve} disabled={busy}>
            {busy ? "Holding your rooms…" : "Reserve"}
          </button>
        </div>
      )}
      {roomCount >= MaxRooms && <p className="muted">For more than {MaxRooms} rooms, please contact the hotel directly.</p>}
    </>
  );
}
