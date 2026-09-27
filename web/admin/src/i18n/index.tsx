import { createContext, Fragment, useContext, useState, type ReactNode } from "react";
import { th } from "./th";

/**
 * English and Thai, switched in the page.
 *
 * The English text is the key. `t("Sign out")` answers the Thai entry in `th.ts` when Thai is
 * chosen and the English otherwise, so a screen reads the same in the source as on the page, and
 * a missing entry shows English rather than a key nobody can read. `i18n.test.ts` fails if any
 * `t(…)` or `m(…)` in the source has no Thai entry, or if an entry is no longer used.
 *
 * What is never translated: anything the server wrote. A refusal is shown in the server's words
 * (`Failure`), and names, titles and bodies are data.
 *
 * `t` is a plain function, not a hook, so helpers and constants can call it at render time without
 * threading a context through. Switching language remounts everything under `LanguageProvider`,
 * which is what makes every `t` call run again. The query cache and the session sit above it and
 * survive; a half-typed form does not, which is an acceptable price for a switch used rarely.
 *
 * The choice is kept in `localStorage`: it is a preference, not a secret, and is per browser.
 */

export type Language = "en" | "th";

export const LANGUAGES: { code: Language; label: string }[] = [
  { code: "en", label: "English" },
  { code: "th", label: "ไทย" },
];

const STORAGE_KEY = "devbuddy.language";

function stored(): Language | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === "en" || value === "th" ? value : null;
  } catch {
    return null;
  }
}

function preferred(): Language {
  const chosen = stored();
  if (chosen) {
    return chosen;
  }

  const languages = typeof navigator === "undefined" ? [] : navigator.languages ?? [navigator.language];
  return languages.some((language) => language?.toLowerCase().startsWith("th")) ? "th" : "en";
}

let current: Language = preferred();

function apply(language: Language) {
  current = language;
  if (typeof document !== "undefined") {
    document.documentElement.lang = language;
  }
}

apply(current);

/** The language every `t` call answers in right now. */
export function language(): Language {
  return current;
}

/**
 * The text in the chosen language. `{name}` in the text is replaced by `values.name`.
 *
 * The argument must be a string literal wherever the text is written, so the test can find it.
 * A value that is decided elsewhere is marked with `m` where it is written instead.
 * A sentence with an element inside it goes through `tr`.
 */
export function t(english: string, values?: Record<string, string | number>): string {
  const text = current === "th" ? (th[english] ?? english) : english;

  return values ? text.replace(/\{(\w+)\}/g, (whole: string, name: string) => String(values[name] ?? whole)) : text;
}

/**
 * A sentence with elements in it: a date, a link, a code span. `{name}` is replaced by
 * `values.name`, which may be any node. The whole sentence is one key, because a translation
 * that has to be assembled from fragments cannot put the words in the order its language needs.
 */
export function tr(english: string, values: Record<string, ReactNode>): ReactNode[] {
  const text = current === "th" ? (th[english] ?? english) : english;

  return text.split(/(\{\w+\})/).map((part, index) => {
    const name = /^\{(\w+)\}$/.exec(part)?.[1];
    return name !== undefined && name in values ? <Fragment key={index}>{values[name]}</Fragment> : part;
  });
}

/**
 * Marks a string for translation without translating it. Used where a label is written into a
 * table or a constant, which is evaluated once, before anybody has chosen a language. The screen
 * passes it through `t` when it renders.
 */
export function m<T extends string>(english: T): T {
  return english;
}

/** A timestamp in the chosen language's conventions. Thai shows the Buddhist year, as Thai does. */
export function formatDateTime(value: string): string {
  return new Date(value).toLocaleString(current === "th" ? "th-TH" : "en-US");
}

const LanguageContext = createContext<{ language: Language; choose: (language: Language) => void }>({
  language: current,
  choose: apply,
});

export function LanguageProvider({ children }: { children: ReactNode }) {
  const [chosen, setChosen] = useState<Language>(current);

  function choose(language: Language) {
    try {
      localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // Private windows and blocked storage: the choice lasts until the page is reloaded.
    }

    apply(language);
    setChosen(language);
  }

  return (
    <LanguageContext.Provider value={{ language: chosen, choose }}>
      <Fragment key={chosen}>{children}</Fragment>
    </LanguageContext.Provider>
  );
}

export function useLanguage() {
  return useContext(LanguageContext);
}
