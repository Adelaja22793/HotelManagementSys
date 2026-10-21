import type { GuestDetails } from "../types/api";

export type GuestErrors = Partial<Record<keyof GuestDetails, string>>;

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
const phonePattern = /^\+?[0-9 ()-]{7,20}$/;

/** Client-side checks for a quick answer. The API checks everything again. */
export function validateGuest(details: GuestDetails): GuestErrors {
  const errors: GuestErrors = {};
  if (!details.firstName.trim()) errors.firstName = "Enter your first name.";
  else if (details.firstName.length > 100) errors.firstName = "First name is too long.";
  if (!details.lastName.trim()) errors.lastName = "Enter your last name.";
  else if (details.lastName.length > 100) errors.lastName = "Last name is too long.";
  if (!emailPattern.test(details.email.trim())) errors.email = "Enter a valid email address for your confirmation.";
  if (details.phone.trim() && !phonePattern.test(details.phone.trim())) errors.phone = "Enter a valid phone number, e.g. +234 801 234 5678.";
  if (details.specialRequests.length > 500) errors.specialRequests = "Special requests are limited to 500 characters.";
  if (!details.acceptTerms) errors.acceptTerms = "Please accept the booking terms and cancellation policy.";
  return errors;
}

export interface SearchInput {
  branchCode: string;
  arrival: string;
  departure: string;
  adults: number;
  children: number;
}

export function validateSearch(input: SearchInput, today: string): string | null {
  if (!input.branchCode) return "Choose a hotel.";
  if (!input.arrival || !input.departure) return "Choose your dates.";
  if (input.arrival < today) return "Arrival cannot be in the past.";
  if (input.departure <= input.arrival) return "Departure must be after arrival.";
  if (input.adults < 1 || input.adults > 10) return "Choose between 1 and 10 adults.";
  if (input.children < 0 || input.children > 10) return "Choose up to 10 children.";
  return null;
}
