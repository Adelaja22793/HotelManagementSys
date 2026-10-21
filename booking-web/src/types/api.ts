// Mirrors HMS.Contracts.PublicBooking. Dates are ISO yyyy-MM-dd strings; money is a number in the branch currency.

export interface PublicBranch {
  code: string;
  name: string;
  city: string | null;
  addressLine: string | null;
  phone: string | null;
  currency: string;
  timeZoneId: string;
}

export interface PublicNight {
  date: string;
  rate: number;
}

export interface RoomOffer {
  roomTypeId: string;
  code: string;
  name: string;
  description: string | null;
  maxAdults: number;
  maxChildren: number;
  available: number;
  ratePlanName: string;
  isRefundable: boolean;
  cancellationPolicy: string;
  currency: string;
  nights: PublicNight[];
  roomSubtotal: number;
  taxAmount: number;
  totalAmount: number;
  amountDueNow: number;
}

export interface Availability {
  branchCode: string;
  arrivalDate: string;
  departureDate: string;
  nights: number;
  adults: number;
  children: number;
  offers: RoomOffer[];
}

export interface HoldRoom {
  roomTypeId: string;
  roomTypeName: string;
  quantity: number;
  ratePlanName: string;
  isRefundable: boolean;
  cancellationPolicy: string;
  totalPerRoom: number;
  depositPerRoom: number;
}

export type HoldStatus = "Active" | "Converted" | "Expired" | "Released" | "NeedsReview";

export interface Hold {
  reference: string;
  accessToken: string | null;
  branchCode: string;
  branchName: string;
  arrivalDate: string;
  departureDate: string;
  adults: number;
  children: number;
  status: HoldStatus;
  expiresAtUtc: string;
  currency: string;
  totalAmount: number;
  amountDueNow: number;
  rooms: HoldRoom[];
  hasGuestDetails: boolean;
  guestName: string | null;
  guestEmail: string | null;
}

export interface GuestDetails {
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  country: string;
  specialRequests: string;
  acceptTerms: boolean;
  /** Honeypot: hidden from people, filled in by bots. Must stay empty. */
  website: string;
}

export interface PaymentInit {
  merchantReference: string;
  provider: string;
  authorizationUrl: string;
  amount: number;
  currency: string;
  holdExpiresAtUtc: string;
}

export type PaymentStatus = "Initialized" | "Succeeded" | "Failed" | "NeedsReview" | "Resolved";

export interface PublicReservation {
  reservationNumber: string;
  roomTypeName: string;
  status: string;
  totalAmount: number;
  cancellationPolicy: string;
}

export interface Booking {
  reference: string;
  status: HoldStatus;
  paymentStatus: PaymentStatus | null;
  branchName: string;
  branchPhone: string | null;
  arrivalDate: string;
  departureDate: string;
  guestName: string | null;
  currency: string;
  totalAmount: number;
  amountPaid: number;
  reservations: PublicReservation[];
  cancellationRequested: boolean;
  message: string | null;
}

export interface ApiErrorBody {
  code: string;
  description: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}
