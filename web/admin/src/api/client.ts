import type { Operations, OperationName, PermissionName } from "./operations";

/**
 * The one place this application talks to the server.
 *
 * Every operation goes through `invoke`, which is the browser-side mirror of the dispatcher: one
 * route, one shape, one place that knows about tokens and refusals. Screens call it with an
 * operation name and get a typed result, and nothing else in the client constructs a request.
 */

/** Where the API lives. Vite proxies `/api` to it in development. */
const BASE = import.meta.env.VITE_DEVBUDDY_API ?? "/api";

/**
 * Tokens.
 *
 * The access token lives in memory only. The refresh token lives in `sessionStorage`, which is a
 * deliberate compromise and worth stating: the server issues bearer tokens rather than setting an
 * HttpOnly cookie, so the browser has to hold one somewhere, and script-readable storage is
 * reachable by any script that gets into the page. `sessionStorage` bounds that to one tab and
 * clears when it closes. Moving refresh onto an HttpOnly cookie would remove the exposure and is
 * the right fix; it is a server change, not a client one.
 */
const REFRESH_KEY = "devbuddy.refresh";

let accessToken: string | null = null;

export interface TokenPair {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

export interface WorkspaceAccess {
  workspaceId: string;
  name: string;
  role: "Viewer" | "Contributor" | "Reviewer" | "Administrator";
  scopedToProject: string | null;
  permissions: PermissionName[];
}

export interface SignedInUser {
  userId: string;
  email: string;
  displayName: string;
  workspaces: WorkspaceAccess[];
}

/** A refusal the server explained. Screens show `detail`; `details` carries validation errors. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail: string,
    readonly details: string[] = [],
  ) {
    super(detail || title);
    this.name = "ApiError";
  }
}

export function storedRefreshToken(): string | null {
  try {
    return sessionStorage.getItem(REFRESH_KEY);
  } catch {
    // Storage can be unavailable outright — a private window with it disabled, or a test
    // environment. Signing in still works; the session just does not survive a reload.
    return null;
  }
}

export function rememberTokens(tokens: TokenPair): void {
  accessToken = tokens.accessToken;

  try {
    sessionStorage.setItem(REFRESH_KEY, tokens.refreshToken);
  } catch {
    /* Not fatal. See above. */
  }
}

export function forgetTokens(): void {
  accessToken = null;

  try {
    sessionStorage.removeItem(REFRESH_KEY);
  } catch {
    /* Not fatal. */
  }
}

export function hasSession(): boolean {
  return accessToken !== null || storedRefreshToken() !== null;
}

async function readProblem(response: Response): Promise<ApiError> {
  let title = response.statusText || "Request failed";
  let detail = "";
  let details: string[] = [];

  try {
    const body = (await response.json()) as {
      title?: string;
      detail?: string;
      details?: string[];
    };

    title = body.title ?? title;
    detail = body.detail ?? "";
    details = body.details ?? [];
  } catch {
    /* Not every failure carries a problem document. */
  }

  return new ApiError(response.status, title, detail, details);
}

async function send(path: string, init: RequestInit, authenticated: boolean): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("content-type", "application/json");

  if (authenticated && accessToken) {
    headers.set("authorization", `Bearer ${accessToken}`);
  }

  return fetch(`${BASE}${path}`, { ...init, headers });
}

/**
 * Exchanges the refresh token for a new pair.
 *
 * Returns false when there is nothing to exchange or the exchange was refused, which the caller
 * treats as "signed out" rather than retrying. A reused or revoked token comes back 401 and the
 * server has already ended that session, so retrying would only end it again.
 */
export async function refreshSession(): Promise<boolean> {
  const refreshToken = storedRefreshToken();

  if (!refreshToken) {
    return false;
  }

  const response = await send(
    "/auth/refresh",
    { method: "POST", body: JSON.stringify({ refreshToken }) },
    false,
  );

  if (!response.ok) {
    forgetTokens();
    return false;
  }

  rememberTokens((await response.json()) as TokenPair);
  return true;
}

async function authenticated(path: string, init: RequestInit): Promise<Response> {
  let response = await send(path, init, true);

  // One retry, and only for an expired access token. Access tokens are short-lived by design, so
  // a 401 in the middle of a session is the expected case rather than an error.
  if (response.status === 401 && (await refreshSession())) {
    response = await send(path, init, true);
  }

  return response;
}

export async function signIn(email: string, password: string): Promise<void> {
  const response = await send(
    "/auth/sign-in",
    { method: "POST", body: JSON.stringify({ email, password }) },
    false,
  );

  if (!response.ok) {
    throw await readProblem(response);
  }

  rememberTokens((await response.json()) as TokenPair);
}

export async function signOut(): Promise<void> {
  const refreshToken = storedRefreshToken();

  if (refreshToken) {
    // Revoked server-side as well as forgotten here. Dropping it locally would leave a live
    // token in the database until it expired.
    await send("/auth/sign-out", { method: "POST", body: JSON.stringify({ refreshToken }) }, false);
  }

  forgetTokens();
}

export async function beginRecovery(email: string): Promise<void> {
  await send("/auth/recovery/begin", { method: "POST", body: JSON.stringify({ email }) }, false);
}

export async function completeRecovery(token: string, newPassword: string): Promise<void> {
  const response = await send(
    "/auth/recovery/complete",
    { method: "POST", body: JSON.stringify({ token, newPassword }) },
    false,
  );

  if (!response.ok) {
    throw await readProblem(response);
  }
}

export async function describeSignedInUser(): Promise<SignedInUser> {
  const response = await authenticated("/me", { method: "GET" });

  if (!response.ok) {
    throw await readProblem(response);
  }

  return (await response.json()) as SignedInUser;
}

/**
 * Runs one operation.
 *
 * The name and both shapes come from the generated client, so calling an operation that does not
 * exist, or passing the wrong arguments, is a compile error rather than a 400 somebody discovers
 * later.
 */
export async function invoke<N extends OperationName>(
  name: N,
  args: Operations[N]["arguments"],
): Promise<Operations[N]["result"]> {
  const response = await authenticated(`/operations/${name}`, {
    method: "POST",
    body: JSON.stringify(args),
  });

  if (!response.ok) {
    throw await readProblem(response);
  }

  return (await response.json()) as Operations[N]["result"];
}

export interface HealthReport {
  isHealthy: boolean;
  components: { name: string; isHealthy: boolean; detail: string | null }[];
  checkedAt: string;
}

/** Liveness only, and anonymous. Component health is an authorised operation because it names components. */
export async function liveness(): Promise<boolean> {
  try {
    const response = await send("/health", { method: "GET" }, false);
    return response.ok;
  } catch {
    return false;
  }
}
