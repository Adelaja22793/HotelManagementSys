/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// VITE_API_BASE_URL points at the HMS API, e.g. https://hms-api.example.com. In development the dev server
// proxies /api to the local API so no CORS set-up is needed.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { "/api": { target: "https://localhost:7180", changeOrigin: true, secure: false } },
  },
  build: { sourcemap: false },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test-setup.ts"],
  },
});
