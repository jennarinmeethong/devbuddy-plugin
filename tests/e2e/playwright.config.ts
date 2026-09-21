import path from "node:path";
import { defineConfig, devices } from "@playwright/test";

/**
 * End-to-end tests over the whole running system: the web client the API host serves, the HTTP
 * API behind it, the MCP server over HTTP, PostgreSQL and the object store.
 *
 * Nothing here is faked. The suite runs against a stack started from `docker/compose.yaml`, and
 * `run.sh` is what starts one, bootstraps it and throws it away afterwards. Pointed at an
 * installation somebody cares about it would create accounts, projects and workspaces there and
 * delete nothing — which is why the URLs have no default beyond loopback.
 *
 * Retries are off. A test that passes on its second attempt is a finding, not a pass.
 */

const output = process.env.DEVBUDDY_E2E_OUTPUT ?? path.join(import.meta.dirname, ".out");

// One place for state the setup project hands to every worker: the accounts it created.
process.env.DEVBUDDY_E2E_STATE ??= path.join(import.meta.dirname, ".state");

export default defineConfig({
  testDir: ".",
  outputDir: path.join(output, "test-results"),
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  workers: Number(process.env.DEVBUDDY_E2E_WORKERS ?? 4),
  timeout: 60_000,
  expect: { timeout: 15_000 },

  reporter: [
    ["list"],
    ["html", { open: "never", outputFolder: path.join(output, "report") }],
    ["junit", { outputFile: path.join(output, "junit.xml") }],
  ],

  use: {
    baseURL: process.env.DEVBUDDY_E2E_BASE_URL ?? "http://127.0.0.1:8080",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },

  projects: [
    // Creates one account per role through the public API, once, before anything else runs.
    { name: "setup", testMatch: /global\.setup\.ts$/ },
    {
      name: "chromium",
      testMatch: /specs[\\/].*\.spec\.ts$/,
      use: { ...devices["Desktop Chrome"] },
      dependencies: ["setup"],
    },
    // Phase 13, C7: the same specs in Firefox and WebKit, so a screen that works in one engine and
    // not another is found here rather than by a person using it.
    {
      name: "firefox",
      testMatch: /specs[\\/].*\.spec\.ts$/,
      use: { ...devices["Desktop Firefox"] },
      dependencies: ["setup"],
    },
    {
      name: "webkit",
      testMatch: /specs[\\/].*\.spec\.ts$/,
      use: { ...devices["Desktop Safari"] },
      dependencies: ["setup"],
    },
  ],
});
