import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SessionProvider } from "./api/session";
import { App } from "./App";
import "./index.css";

const queries = new QueryClient({
  defaultOptions: {
    queries: {
      // A refusal is not a transient failure, and retrying one three times turns a clear 403 into
      // a slow one. Nothing here is worth retrying that a person cannot retry themselves.
      retry: false,
      refetchOnWindowFocus: false,
    },
  },
});

const container = document.getElementById("root");

if (!container) {
  throw new Error("The page has no #root element to mount into.");
}

createRoot(container).render(
  <StrictMode>
    <QueryClientProvider client={queries}>
      <BrowserRouter>
        <SessionProvider>
          <App />
        </SessionProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
