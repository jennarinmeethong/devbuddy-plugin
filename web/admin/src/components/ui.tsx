import type {
  ButtonHTMLAttributes,
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from "react";
import { formatDateTime } from "../i18n";

/**
 * The small set of pieces every screen is built from.
 *
 * Written here rather than pulled from a component library. The whole visual surface of this
 * application is a form, a table, and a panel; a dependency would bring several hundred
 * components to supply three, and every one of them would be a thing to keep updated.
 */

function join(...classes: (string | false | undefined)[]): string {
  return classes.filter(Boolean).join(" ");
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "secondary" | "danger";
};

export function Button({ variant = "secondary", className, ...rest }: ButtonProps) {
  const styles = {
    primary: [
      "border border-white/70 bg-linear-145 from-[var(--color-accent-bright)] to-[var(--color-accent)]",
      "text-[var(--color-on-accent)] shadow-small hover:brightness-110",
    ].join(" "),
    secondary: [
      "border border-[var(--color-line-strong)] bg-[var(--color-soft)] text-[var(--color-ink)]",
      "hover:bg-[var(--color-hover)]",
    ].join(" "),
    danger: [
      "border border-[var(--color-danger)] bg-[var(--color-surface)] text-[var(--color-danger)]",
      "hover:bg-[var(--color-danger-soft)]",
    ].join(" "),
  }[variant];

  return (
    <button
      type="button"
      className={join(
        "inline-flex items-center gap-2 rounded-lg px-3.5 py-1.5 text-sm font-medium whitespace-nowrap transition",
        "disabled:cursor-not-allowed disabled:opacity-50",
        styles,
        className,
      )}
      {...rest}
    />
  );
}

export function Field({
  label,
  hint,
  children,
}: {
  label: string;
  hint?: ReactNode;
  children: ReactNode;
}) {
  return (
    <label className="block space-y-1">
      <span className="block text-sm font-semibold">{label}</span>
      {children}
      {hint ? <span className="block text-xs text-[var(--color-muted)]">{hint}</span> : null}
    </label>
  );
}

export function Input({ className, ...rest }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={join(
        "w-full rounded-lg border border-[var(--color-line-strong)] bg-[var(--color-surface)] px-3 py-1.5 text-sm",
        "hover:border-[var(--color-accent-bright)]/50",
        "focus:border-[var(--color-accent)] focus:outline-none focus:ring-3 focus:ring-[var(--color-accent-soft)]",
        className,
      )}
      {...rest}
    />
  );
}

export function TextArea({ className, ...rest }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <textarea
      className={join(
        "w-full rounded-lg border border-[var(--color-line-strong)] bg-[var(--color-surface)] px-3 py-1.5 text-sm",
        "hover:border-[var(--color-accent-bright)]/50",
        "focus:border-[var(--color-accent)] focus:outline-none focus:ring-3 focus:ring-[var(--color-accent-soft)]",
        className,
      )}
      {...rest}
    />
  );
}

export function Select({ className, children, ...rest }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select
      className={join(
        "w-full rounded-lg border border-[var(--color-line-strong)] bg-[var(--color-surface)] px-3 py-1.5 text-sm",
        "hover:border-[var(--color-accent-bright)]/50",
        "focus:border-[var(--color-accent)] focus:outline-none focus:ring-3 focus:ring-[var(--color-accent-soft)]",
        className,
      )}
      {...rest}
    >
      {children}
    </select>
  );
}

export function Panel({ title, actions, children }: { title: string; actions?: ReactNode; children: ReactNode }) {
  return (
    <section className="overflow-hidden rounded-xl border border-[var(--color-line)] bg-[var(--color-surface)] shadow-panel">
      <header className="flex items-center justify-between gap-3 border-b border-[var(--color-line)] bg-[var(--color-soft)] px-5 py-3">
        <h2 className="text-base font-semibold">{title}</h2>
        {actions}
      </header>
      <div className="p-5">{children}</div>
    </section>
  );
}

export function Alert({ tone = "info", children }: { tone?: "info" | "error" | "success"; children: ReactNode }) {
  const styles = {
    info: "border-[var(--color-line)] bg-[var(--color-soft)] text-[var(--color-ink)]",
    error: "border-[var(--color-danger)] bg-[var(--color-danger-soft)] text-[var(--color-danger)]",
    success: "border-[var(--color-success)] bg-[var(--color-success-soft)] text-[var(--color-success)]",
  }[tone];

  return (
    <div role={tone === "error" ? "alert" : "status"} className={join("rounded-lg border px-3 py-2 text-sm", styles)}>
      {children}
    </div>
  );
}

export function Badge({ children, tone = "neutral" }: { children: ReactNode; tone?: "neutral" | "live" | "muted" }) {
  const styles = {
    neutral: "border-[var(--color-line)] bg-[var(--color-accent-soft)] text-[var(--color-accent)]",
    live: "border-[var(--color-success)]/40 bg-[var(--color-success-soft)] text-[var(--color-success)]",
    muted: "border-[var(--color-line)] bg-[var(--color-soft)] text-[var(--color-muted)] line-through",
  }[tone];

  return <span className={join("rounded-md border px-1.5 py-0.5 text-xs font-medium whitespace-nowrap", styles)}>{children}</span>;
}

/**
 * A table that scrolls rather than squeezes.
 *
 * Wider than the panel, it scrolls sideways instead of wrapping every cell to a word per line; a
 * minimum width, for the table and for each cell, means a phone gets the same columns as a desktop
 * and no column is squeezed to a word a line. Longer than about two thirds of
 * the screen, it scrolls inside the panel with the header held at the top, so a long audit window
 * does not push the form below it off the page. The scrolling box can take focus, so it can be
 * scrolled from the keyboard.
 */
export function Table({ head, children }: { head: string[]; children: ReactNode }) {
  return (
    <div tabIndex={0} className="max-h-[70vh] overflow-auto overscroll-contain rounded-lg">
      <table className="w-full min-w-[40rem] text-left text-sm [&_td]:min-w-24">
        <thead className="sticky top-0 z-[1] bg-[var(--color-surface)]">
          <tr className="border-b border-[var(--color-line-strong)] text-xs tracking-wide text-[var(--color-muted)] uppercase">
            {head.map((column, index) => (
              <th key={`${index}-${column}`} scope="col" className="px-2 py-2 font-medium whitespace-nowrap">
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="py-6 text-center text-sm text-[var(--color-muted)]">{children}</p>;
}

/** A content hash, shown in full. An approval binds to this exact value, so it is never elided. */
export function Hash({ value }: { value: string }) {
  return <code className="break-all font-mono text-xs text-[var(--color-muted)]">{value}</code>;
}

export function When({ value }: { value: string | null | undefined }) {
  if (!value) {
    return <span className="text-[var(--color-muted)]">—</span>;
  }

  return <time dateTime={value}>{formatDateTime(value)}</time>;
}

/** The letter mark beside the product name, from the Ocean Mist Light demo. Decoration only. */
export function BrandMark() {
  return (
    <span
      aria-hidden="true"
      className={join(
        "grid size-10 shrink-0 place-items-center rounded-[11px] border border-white/70 font-serif text-xl font-bold",
        "bg-linear-145 from-[var(--color-accent-bright)] to-[var(--color-accent)] text-[var(--color-on-accent)]",
        "shadow-[0_7px_20px_rgba(13,59,102,0.25)]",
      )}
    >
      D
    </span>
  );
}

const icons = {
  projects: "M4 13h6V4H4v9Zm0 7h6v-4H4v4Zm10 0h6v-9h-6v9Zm0-16v4h6V4h-6Z",
  members:
    "M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5s-3 1.34-3 3 1.34 3 3 3Zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5 5 6.34 5 8s1.34 3 3 3Zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5C15 14.17 10.33 13 8 13Zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5Z",
  teams:
    "M12 1 3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4Zm0 4a3 3 0 1 1 0 6 3 3 0 0 1 0-6Zm0 14.9a8.5 8.5 0 0 1-5-3.79c.03-1.66 3.34-2.57 5-2.57 1.65 0 4.97.91 5 2.57a8.5 8.5 0 0 1-5 3.79Z",
  workspaces:
    "m11.99 18.54-7.37-5.73L3 14.07l9 7 9-7-1.63-1.27-7.38 5.74ZM12 16l7.36-5.73L21 9l-9-7-9 7 1.63 1.27L12 16Z",
  audit:
    "M13 3a9 9 0 1 0 8 8h-8V3Zm-2 2.07A7 7 0 1 1 5.07 11H11V5.07ZM15 3.23V9h5.77A9.02 9.02 0 0 0 15 3.23Z",
  health:
    "m12 21.35-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35Z",
  key: "M12.65 10A5.99 5.99 0 0 0 7 6c-3.31 0-6 2.69-6 6s2.69 6 6 6a5.99 5.99 0 0 0 5.65-4H17v4h4v-4h2v-4H12.65ZM7 14c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2Z",
  menu: "M3 6h18v2H3V6Zm0 5h18v2H3v-2Zm0 5h18v2H3v-2Z",
  close:
    "M18.3 5.71 12 12l6.3 6.29-1.42 1.42L10.59 13.4 4.29 19.7l-1.41-1.42L9.17 12 2.88 5.7l1.41-1.42 6.3 6.3 6.29-6.3 1.42 1.43Z",
} as const;

export type IconName = keyof typeof icons;

/** A navigation icon. Hidden from assistive technology: the link's text is its name. */
export function Icon({ name, className }: { name: IconName; className?: string }) {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true" className={join("size-[18px] shrink-0 fill-current", className)}>
      <path d={icons[name]} />
    </svg>
  );
}
