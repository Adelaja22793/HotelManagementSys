import { BrowserRouter, Route, Routes } from "react-router-dom";
import { Layout } from "../components/Layout";
import { AvailabilityPage } from "./routes/AvailabilityPage";
import { BookingConfirmationPage } from "./routes/BookingConfirmationPage";
import { GuestDetailsPage } from "./routes/GuestDetailsPage";
import { ManageBookingPage } from "./routes/ManageBookingPage";
import { PaymentPage } from "./routes/PaymentPage";
import { SearchPage } from "./routes/SearchPage";

export function AppRoutes() {
  return (
    <Layout>
      <Routes>
        <Route path="/" element={<SearchPage />} />
        <Route path="/availability" element={<AvailabilityPage />} />
        <Route path="/booking/:reference/guest" element={<GuestDetailsPage />} />
        <Route path="/booking/:reference/pay" element={<PaymentPage />} />
        <Route path="/booking/:reference/payment-return" element={<BookingConfirmationPage />} />
        <Route path="/booking/:reference" element={<ManageBookingPage />} />
        <Route path="*" element={<p>Page not found.</p>} />
      </Routes>
    </Layout>
  );
}

export function App() {
  return (
    <BrowserRouter>
      <AppRoutes />
    </BrowserRouter>
  );
}
