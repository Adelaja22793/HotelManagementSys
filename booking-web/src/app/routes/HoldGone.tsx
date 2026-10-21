import { Link } from "react-router-dom";
import type { Hold } from "../../types/api";

/** Shown when a hold can no longer be continued. */
export function HoldGone({ hold }: { hold: Hold }) {
  if (hold.status === "Converted")
    return (
      <div className="card">
        <p>This booking is already confirmed.</p>
        <Link to={`/booking/${hold.reference}`}>View your booking</Link>
      </div>
    );
  const search = new URLSearchParams({ branch: hold.branchCode, arrival: hold.arrivalDate, departure: hold.departureDate,
    adults: String(hold.adults), children: String(hold.children) });
  return (
    <div className="card">
      <p>{hold.status === "Released" ? "You cancelled this booking before paying." : "Your hold has expired and the rooms have been released."}</p>
      <Link className="button primary" to={`/availability?${search}`}>Search again</Link>
    </div>
  );
}
