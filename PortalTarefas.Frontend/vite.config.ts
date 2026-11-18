import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Proxy de desenvolvimento: o navegador chama a mesma origem (sem CORS)
export default defineConfig({
  plugins: [react()],
  server: { proxy: { "/api": "http://localhost:5001" } },
});
