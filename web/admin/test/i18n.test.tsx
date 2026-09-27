import { afterEach, describe, expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { Glob } from "bun";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { LanguageProvider, language, t } from "../src/i18n";
import { th } from "../src/i18n/th";
import { LanguageSwitch } from "../src/components/LanguageSwitch";

/**
 * The Thai dictionary against the source.
 *
 * The English text is the key, so the only way an entry goes missing is somebody writing a new
 * sentence and not translating it, and the only way one goes stale is somebody rewording the
 * English. Both are caught here rather than by a Thai reader finding English on a Thai screen.
 */

const SRC = join(import.meta.dir, "..", "src");

/** Every string literal passed to `t`, `tr` or `m` anywhere in the client. */
function keys(): Map<string, string> {
  const found = new Map<string, string>();
  for (const file of new Glob("**/*.{ts,tsx}").scanSync(SRC)) {
    const source = readFileSync(join(SRC, file), "utf8");
    for (const match of source.matchAll(/(?<![\w.])(?:t|m|tr)\(\s*("(?:[^"\\\n]|\\.)*")/g)) {
      found.set(JSON.parse(match[1]!) as string, file);
    }
  }
  return found;
}

function placeholders(text: string): string[] {
  return [...text.matchAll(/\{(\w+)\}/g)].map((match) => match[1]!).sort();
}

describe("the Thai dictionary", () => {
  const used = keys();

  test("finds the keys it is checking", () => {
    // A broken pattern would find nothing and pass everything below.
    expect(used.size).toBeGreaterThan(300);
    expect(used.has("Sign out")).toBe(true);
  });

  test("translates every key the client uses", () => {
    const missing = [...used].filter(([key]) => !(key in th)).map(([key, file]) => `${file}: ${key}`);
    expect(missing).toEqual([]);
  });

  test("holds no entry the client no longer uses", () => {
    expect(Object.keys(th).filter((key) => !used.has(key))).toEqual([]);
  });

  test("keeps every placeholder of the English", () => {
    const wrong = Object.entries(th)
      .filter(([english, thai]) => placeholders(english).join() !== placeholders(thai).join())
      .map(([english]) => english);
    expect(wrong).toEqual([]);
  });

  test("leaves no entry empty", () => {
    const empty = Object.entries(th).filter(([, thai]) => thai.trim() === "");
    expect(empty).toEqual([]);
  });
});

/** A screen in miniature: it calls `t` when it renders, which is what the remount reruns. */
function SignOutLabel() {
  return <p>{t("Sign out")}</p>;
}

describe("switching language", () => {
  // The chosen language is module state, shared with every test file run after this one, so it is
  // put back to English through the switch before the switch is unmounted.
  afterEach(() => {
    const english = screen.queryByRole("button", { name: "English" });
    if (english) {
      act(() => {
        fireEvent.click(english);
      });
    }
    cleanup();
    localStorage.clear();
    expect(language()).toBe("en");
  });

  test("answers in Thai once Thai is chosen, and remembers the choice", () => {
    render(
      <LanguageProvider>
        <LanguageSwitch />
        <SignOutLabel />
      </LanguageProvider>,
    );

    expect(screen.getByText("Sign out")).toBeDefined();
    act(() => {
      fireEvent.click(screen.getByRole("button", { name: "ไทย" }));
    });

    expect(language()).toBe("th");
    expect(screen.getByText(th["Sign out"]!)).toBeDefined();
    expect(screen.getByRole("button", { name: "ไทย" }).getAttribute("aria-pressed")).toBe("true");
    expect(document.documentElement.lang).toBe("th");
    expect(localStorage.getItem("devbuddy.language")).toBe("th");
  });

  test("fills a placeholder, and falls back to the English for text it has no entry for", () => {
    expect(t("Granted {role}.", { role: "Viewer" })).toBe("Granted Viewer.");
    expect(t("A sentence nobody wrote")).toBe("A sentence nobody wrote");
  });
});
