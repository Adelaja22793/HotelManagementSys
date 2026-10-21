import { longDate, money, nightsBetween } from "../services/format";
import type { Hold } from "../types/api";

export function StaySummary({ hold }: { hold: Hold }) {
  const nights = nightsBetween(hold.arrivalDate, hold.departureDate);
  return (
    <aside className="card summary">
      <h2>{hold.branchName}</h2>
      <p>
        {longDate(hold.arrivalDate)} to {longDate(hold.departureDate)} · {nights} night{nights === 1 ? "" : "s"}
      </p>
      <p>
        {hold.adults} adult{hold.adults === 1 ? "" : "s"}
        {hold.children > 0 ? `, ${hold.children} child${hold.children === 1 ? "" : "ren"}` : ""}
      </p>
      <ul className="summary-rooms">
        {hold.rooms.map((room) => (
          <li key={room.roomTypeId}>
            {room.quantity} × {room.roomTypeName} ({room.ratePlanName}) — {money(room.totalPerRoom * room.quantity, hold.currency)}
            <div className="muted">{room.cancellationPolicy}</div>
          </li>
        ))}
      </ul>
      <p className="total">
        Total {money(hold.totalAmount, hold.currency)}
        {hold.amountDueNow !== hold.totalAmount && <span className="muted"> · due now {money(hold.amountDueNow, hold.currency)}</span>}
      </p>
      <p className="muted">Booking code {hold.reference}</p>
    </aside>
  );
}
