import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// The API is a separate origin in development. Proxying it here rather than enabling CORS on the
// server keeps the browser talking to one origin, which is also how it is deployed: a reverse
// proxy in front of both. A CORS policy wide enough for development has a habit of shipping.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      "/api": {
        target: process.env.DEVBUDDY_API ?? "http://localhost:5288",
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ""),
      },
    },
  },
  build: { outDir: "dist", sourcemap: true },
});
