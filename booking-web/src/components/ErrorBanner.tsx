import { ApiError } from "../services/api";

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const fields = Object.values(error.fieldErrors).flat();
    return fields.length > 0 ? fields.join(" ") : error.message;
  }
  return "Something went wrong. Please try again.";
}

export function ErrorBanner({ error }: { error: unknown }) {
  if (!error) return null;
  return (
    <div className="banner banner-error" role="alert">
      {errorMessage(error)}
    </div>
  );
}
