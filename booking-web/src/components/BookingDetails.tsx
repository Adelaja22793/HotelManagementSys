import { longDate, money } from "../services/format";
import type { Booking } from "../types/api";

export function BookingDetails({ booking }: { booking: Booking }) {
  return (
    <div className="card">
      <h2>{booking.branchName}</h2>
      <p>
        {longDate(booking.arrivalDate)} to {longDate(booking.departureDate)}
        {booking.guestName ? ` · ${booking.guestName}` : ""}
      </p>
      {booking.reservations.length > 0 && (
        <table className="reservations">
          <thead>
            <tr>
              <th>Reservation</th>
              <th>Room</th>
              <th>Status</th>
              <th>Total</th>
            </tr>
          </thead>
          <tbody>
            {booking.reservations.map((r) => (
              <tr key={r.reservationNumber}>
                <td>{r.reservationNumber}</td>
                <td>{r.roomTypeName}</td>
                <td>{r.status}</td>
                <td>{money(r.totalAmount, booking.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <p className="total">
        Total {money(booking.totalAmount, booking.currency)} · paid {money(booking.amountPaid, booking.currency)}
      </p>
      {booking.branchPhone && <p className="muted">Questions? Call the hotel on {booking.branchPhone}.</p>}
    </div>
  );
}
