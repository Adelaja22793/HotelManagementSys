import { useState, type FormEvent } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { ErrorBanner } from "../../components/ErrorBanner";
import { Steps } from "../../components/Steps";
import { useAsync } from "../../components/useAsync";
import { api } from "../../services/api";
import { addDays, todayIso } from "../../services/format";
import { validateSearch } from "../../validation/guest";

export function SearchPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const today = todayIso();
  const branches = useAsync(() => api.branches(), []);
  const [branchCode, setBranchCode] = useState(params.get("branch") ?? "");
  const [arrival, setArrival] = useState(params.get("arrival") ?? addDays(today, 1));
  const [departure, setDeparture] = useState(params.get("departure") ?? addDays(today, 2));
  const [adults, setAdults] = useState(Number(params.get("adults") ?? 2));
  const [children, setChildren] = useState(Number(params.get("children") ?? 0));
  const [problem, setProblem] = useState<string | null>(null);

  const selected = branchCode || (branches.data?.length === 1 ? branches.data[0]!.code : "");

  function submit(event: FormEvent) {
    event.preventDefault();
    const input = { branchCode: selected, arrival, departure, adults, children };
    const error = validateSearch(input, today);
    setProblem(error);
    if (error) return;
    const query = new URLSearchParams({ branch: selected, arrival, departure, adults: String(adults), children: String(children) });
    navigate(`/availability?${query}`);
  }

  return (
    <>
      <Steps current={0} />
      <h1>Find a room</h1>
      <ErrorBanner error={branches.error} />
      <form className="card form" onSubmit={submit} noValidate>
        <label>
          Hotel
          <select value={selected} onChange={(e) => setBranchCode(e.target.value)} disabled={branches.loading} required>
            <option value="">{branches.loading ? "Loading hotels…" : "Choose a hotel"}</option>
            {branches.data?.map((b) => (
              <option key={b.code} value={b.code}>
                {b.name}
                {b.city ? `, ${b.city}` : ""}
              </option>
            ))}
          </select>
        </label>
        <div className="row">
          <label>
            Arrival
            <input type="date" value={arrival} min={today} onChange={(e) => {
              setArrival(e.target.value);
              if (e.target.value >= departure) setDeparture(addDays(e.target.value, 1));
            }} required />
          </label>
          <label>
            Departure
            <input type="date" value={departure} min={addDays(arrival, 1)} onChange={(e) => setDeparture(e.target.value)} required />
          </label>
        </div>
        <div className="row">
          <label>
            Adults
            <input type="number" min={1} max={10} value={adults} onChange={(e) => setAdults(Number(e.target.value))} />
          </label>
          <label>
            Children
            <input type="number" min={0} max={10} value={children} onChange={(e) => setChildren(Number(e.target.value))} />
          </label>
        </div>
        {problem && <p className="field-error" role="alert">{problem}</p>}
        <button type="submit" className="primary">Search availability</button>
      </form>
    </>
  );
}
