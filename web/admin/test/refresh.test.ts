import { afterEach, beforeEach, expect, test } from "bun:test";
import { forgetTokens, refreshSession, rememberTokens } from "../src/api/client";

/**
 * Refreshing is single-flight.
 *
 * A refresh token is single-use and rotates, and presenting one twice is treated as theft: the
 * server revokes the whole family and the person is signed out. Two callers refreshing at the same
 * moment is not theft, though — it is a page that mounted and fired three queries and got three
 * 401s at once. This is the guard for that, and it is here because it is what actually happened:
 * the session died on every page reload, in the browser, for a reason that looked like nothing.
 */

let exchanges = 0;
let original: typeof globalThis.fetch;

beforeEach(() => {
  exchanges = 0;
  original = globalThis.fetch;
  sessionStorage.clear();

  rememberTokens({
    accessToken: "access",
    accessTokenExpiresAt: "2099-01-01T00:00:00+00:00",
    refreshToken: "the-only-one",
    refreshTokenExpiresAt: "2099-01-01T00:00:00+00:00",
  });

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = typeof input === "string" ? input : input.toString();

    if (path !== "/auth/refresh") {
      throw new Error(`The refresh test does not serve ${path}.`);
    }

    const presented = JSON.parse(String(init?.body)).refreshToken as string;

    // The server's rule, faithfully: the second presentation of a token is refused.
    if (presented !== "the-only-one" || exchanges > 0) {
      exchanges += 1;
      return new Response("{}", { status: 401 });
    }

    exchanges += 1;

    return new Response(
      JSON.stringify({
        accessToken: "access-2",
        accessTokenExpiresAt: "2099-01-01T00:00:00+00:00",
        refreshToken: "the-next-one",
        refreshTokenExpiresAt: "2099-01-01T00:00:00+00:00",
      }),
      { status: 200, headers: { "content-type": "application/json" } },
    );
  }) as typeof fetch;
});

afterEach(() => {
  globalThis.fetch = original;
  forgetTokens();
});

test("callers refreshing at the same moment share one exchange", async () => {
  const results = await Promise.all([refreshSession(), refreshSession(), refreshSession()]);

  expect(results).toEqual([true, true, true]);

  // One, not three. Three would mean two of them presented a spent token, which the server reads
  // as theft and answers by ending the session.
  expect(exchanges).toBe(1);
});

test("a later refresh uses the token the previous one produced", async () => {
  expect(await refreshSession()).toBe(true);

  // The rotated token is what is stored now, so the next exchange presents that rather than the
  // one that was already spent.
  expect(sessionStorage.getItem("devbuddy.refresh")).toBe("the-next-one");
});
