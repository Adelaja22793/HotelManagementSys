const steps = ["Search", "Choose room", "Your details", "Payment", "Confirmed"];

export function Steps({ current }: { current: number }) {
  return (
    <ol className="steps" aria-label="Booking progress">
      {steps.map((label, i) => (
        <li key={label} className={i === current ? "step step-current" : i < current ? "step step-done" : "step"} aria-current={i === current ? "step" : undefined}>
          {label}
        </li>
      ))}
    </ol>
  );
}
