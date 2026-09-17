import { randomBytes } from "node:crypto";

/**
 * Where the system under test is, and who may sign in to it.
 *
 * The administrator is the one `bootstrap` created. Everybody else the suite needs is created by
 * the setup project through the public API, the way an administrator would.
 */
export const env = {
  baseURL: process.env.DEVBUDDY_E2E_BASE_URL ?? "http://127.0.0.1:8080",
  mcpURL: process.env.DEVBUDDY_E2E_MCP_URL ?? "http://127.0.0.1:8081",
  adminEmail: required("DEVBUDDY_E2E_ADMIN_EMAIL"),
  adminPassword: required("DEVBUDDY_E2E_ADMIN_PASSWORD"),
  stateDirectory: required("DEVBUDDY_E2E_STATE"),

  /**
   * The directory the API reads working copies from, writable from here. Null when the suite is
   * pointed at a stack whose projects directory it cannot reach; the analysis tests skip then.
   */
  projectsRoot: process.env.DEVBUDDY_E2E_PROJECTS || null,
};

function required(name: string): string {
  const value = process.env[name];

  if (!value) {
    throw new Error(`${name} is not set. run.sh sets it; see tests/e2e/README.md.`);
  }

  return value;
}

/** A name nobody else in this run, or an earlier run against the same stack, has used. */
export function unique(prefix: string): string {
  return `${prefix} ${randomBytes(4).toString("hex")}`;
}

/** A password long enough for the server's only rule, and random enough to be nobody's. */
export function password(): string {
  return randomBytes(18).toString("base64url");
}
