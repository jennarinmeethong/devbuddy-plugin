import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// The API is a separate origin in development. Proxying it here rather than enabling CORS on the
// server keeps the browser talking to one origin, which is also how it is deployed: the API host
// serves the built files itself. A CORS policy wide enough for development has a habit of
// shipping.
//
// What is proxied is the API's own top-level prefixes rather than an invented `/api`, because the
// built client calls them at the root and development has to look like the thing it is standing in
// for. They cannot collide with the client's own routes, which are `/`, `/set-password`, and
// everything under `/w/`.
const api = {
  target: process.env.DEVBUDDY_API ?? "http://localhost:5288",
  changeOrigin: true,
};

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      "/auth": api,
      "/me": api,
      "/operations": api,
      "/workspaces": api,
    },
  },

  // No source maps: these files are served by the API to anybody who reaches the sign-in page, and
  // a map is the whole client in readable form. Turn them back on locally when debugging a build.
  build: { outDir: "dist", sourcemap: false },
});
