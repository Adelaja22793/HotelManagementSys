// Booking access tokens. They prove the guest owns a booking, so they are kept in this browser only and are put in
// the URL fragment (never sent to servers or written to logs) when the guest saves a "manage my booking" link.

const key = (reference: string) => `hms.booking.${reference.toUpperCase()}`;

function storage(): Storage | null {
  try {
    return window.localStorage;
  } catch {
    return null; // private mode or blocked storage: the guest can still use the link with the token
  }
}

export const tokens = {
  get(reference: string): string | null {
    return storage()?.getItem(key(reference)) ?? null;
  },
  set(reference: string, token: string): void {
    try {
      storage()?.setItem(key(reference), token);
    } catch {
      /* storage full or blocked */
    }
  },
  /** Reads a token from a manage link like /booking/ABC#t=token and remembers it. */
  adoptFromHash(reference: string, hash: string): void {
    const match = /(?:^#|&)t=([A-Za-z0-9_-]{20,100})/.exec(hash);
    if (match?.[1]) tokens.set(reference, match[1]);
  },
  manageLink(reference: string): string | null {
    const token = tokens.get(reference);
    return token ? `${window.location.origin}/booking/${reference}#t=${token}` : null;
  },
};
