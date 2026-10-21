import type { ReactNode } from "react";
import { Link } from "react-router-dom";

export function Layout({ children }: { children: ReactNode }) {
  return (
    <div className="page">
      <header className="site-header">
        <Link to="/" className="brand">
          Book your stay
        </Link>
        <span className="secure-note">Secure booking · Payments by Paystack</span>
      </header>
      <main className="content">{children}</main>
      <footer className="site-footer">
        Card details are entered on Paystack's secure page and never reach the hotel's systems.
      </footer>
    </div>
  );
}
