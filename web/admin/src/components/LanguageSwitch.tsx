import { LANGUAGES, t, useLanguage } from "../i18n";

/**
 * English or Thai. Two buttons rather than a menu: there are two languages, and each button names
 * its language in that language, so somebody who cannot read the current one can still find theirs.
 */
export function LanguageSwitch() {
  const { language, choose } = useLanguage();

  return (
    <div
      role="group"
      aria-label={t("Language")}
      className="inline-flex shrink-0 rounded-lg border border-[var(--color-line)] bg-[var(--color-soft)] p-0.5 text-xs"
    >
      {LANGUAGES.map((option) => (
        <button
          key={option.code}
          type="button"
          lang={option.code}
          aria-pressed={language === option.code}
          onClick={() => choose(option.code)}
          className={[
            "rounded-md px-2 py-1 font-medium transition",
            language === option.code
              ? "bg-[var(--color-accent)] text-[var(--color-on-accent)] shadow-small"
              : "text-[var(--color-muted)] hover:text-[var(--color-ink)]",
          ].join(" ")}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}
