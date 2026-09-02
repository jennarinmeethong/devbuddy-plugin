import type {
  ButtonHTMLAttributes,
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from "react";

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
    primary: "bg-[var(--color-accent)] text-white hover:opacity-90",
    secondary: "bg-white text-[var(--color-ink)] border border-[var(--color-line)] hover:bg-neutral-50",
    danger: "bg-white text-[var(--color-danger)] border border-[var(--color-danger)] hover:bg-red-50",
  }[variant];

  return (
    <button
      type="button"
      className={join(
        "inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium",
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
      <span className="block text-sm font-medium">{label}</span>
      {children}
      {hint ? <span className="block text-xs text-[var(--color-muted)]">{hint}</span> : null}
    </label>
  );
}

export function Input({ className, ...rest }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={join(
        "w-full rounded-md border border-[var(--color-line)] bg-white px-3 py-1.5 text-sm",
        "focus:border-[var(--color-accent)] focus:outline-none",
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
        "w-full rounded-md border border-[var(--color-line)] bg-white px-3 py-1.5 text-sm",
        "focus:border-[var(--color-accent)] focus:outline-none",
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
        "w-full rounded-md border border-[var(--color-line)] bg-white px-3 py-1.5 text-sm",
        "focus:border-[var(--color-accent)] focus:outline-none",
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
    <section className="rounded-lg border border-[var(--color-line)] bg-white">
      <header className="flex items-center justify-between border-b border-[var(--color-line)] px-4 py-3">
        <h2 className="text-sm font-semibold">{title}</h2>
        {actions}
      </header>
      <div className="p-4">{children}</div>
    </section>
  );
}

export function Alert({ tone = "info", children }: { tone?: "info" | "error" | "success"; children: ReactNode }) {
  const styles = {
    info: "border-[var(--color-line)] bg-neutral-50 text-[var(--color-ink)]",
    error: "border-[var(--color-danger)] bg-red-50 text-[var(--color-danger)]",
    success: "border-emerald-300 bg-emerald-50 text-emerald-900",
  }[tone];

  return (
    <div role={tone === "error" ? "alert" : "status"} className={join("rounded-md border px-3 py-2 text-sm", styles)}>
      {children}
    </div>
  );
}

export function Badge({ children, tone = "neutral" }: { children: ReactNode; tone?: "neutral" | "live" | "muted" }) {
  const styles = {
    neutral: "bg-neutral-100 text-[var(--color-ink)]",
    live: "bg-emerald-100 text-emerald-900",
    muted: "bg-neutral-100 text-[var(--color-muted)] line-through",
  }[tone];

  return <span className={join("rounded px-1.5 py-0.5 text-xs font-medium", styles)}>{children}</span>;
}

export function Table({ head, children }: { head: string[]; children: ReactNode }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-[var(--color-line)] text-xs uppercase tracking-wide text-[var(--color-muted)]">
            {head.map((column) => (
              <th key={column} scope="col" className="px-2 py-2 font-medium">
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

  return <time dateTime={value}>{new Date(value).toLocaleString()}</time>;
}
