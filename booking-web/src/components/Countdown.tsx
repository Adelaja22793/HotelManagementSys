import { useEffect, useState } from "react";

/** Shows how long the rooms stay held. Calls onExpired once when time runs out. */
export function Countdown({ expiresAtUtc, onExpired }: { expiresAtUtc: string; onExpired?: () => void }) {
  const target = Date.parse(expiresAtUtc);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  const left = Math.max(0, Math.floor((target - now) / 1000));
  useEffect(() => {
    if (left === 0) onExpired?.();
  }, [left, onExpired]);

  const minutes = Math.floor(left / 60);
  const seconds = String(left % 60).padStart(2, "0");
  return (
    <p className={left < 120 ? "countdown countdown-urgent" : "countdown"} aria-live="polite">
      {left > 0 ? `Your rooms are held for ${minutes}:${seconds}` : "Your hold has expired."}
    </p>
  );
}
