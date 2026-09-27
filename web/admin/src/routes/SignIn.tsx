import { useState } from "react";
import { Link } from "react-router-dom";
import { ApiError, beginRecovery, signIn } from "../api/client";
import { useSession } from "../api/session";
import { Alert, BrandMark, Button, Field, Input, Panel } from "../components/ui";
import { LanguageSwitch } from "../components/LanguageSwitch";
import { t } from "../i18n";

/**
 * Sign in, and start account recovery.
 *
 * The recovery form always says the same thing whatever address is typed. The server answers
 * identically too — that is where the control lives (SB-15) — and a page that said "no such
 * account" would hand back exactly what the server refused to.
 */
export function SignIn() {
  const { reload } = useSession();
  const [mode, setMode] = useState<"sign-in" | "recover">("sign-in");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      if (mode === "sign-in") {
        await signIn(email, password);
        await reload();
      } else {
        await beginRecovery(email);
        setSent(true);
      }
    } catch (failure) {
      setError(
        failure instanceof ApiError
          ? failure.detail || failure.title
          : t("The server could not be reached."),
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mx-auto max-w-md px-5 py-16">
      <div className="mb-8 flex items-center gap-3">
        <BrandMark />
        <div className="flex-1 leading-tight">
          <h1 className="text-xl font-bold tracking-wide">DevBuddy</h1>
          <p className="text-xs tracking-[0.18em] text-[var(--color-muted)]">{t("ADMINISTRATION")}</p>
        </div>
        <LanguageSwitch />
      </div>

      <Panel title={mode === "sign-in" ? t("Sign in") : t("Recover your account")}>
        <form className="space-y-4" onSubmit={submit}>
          {error ? <Alert tone="error">{error}</Alert> : null}

          {sent ? (
            <Alert tone="success">
              {t("If that address has an account, a recovery token has been issued. It is delivered out of band — ask whoever runs this installation for it, then use the link below.")}
            </Alert>
          ) : null}

          <Field label={t("Email")}>
            <Input
              type="email"
              name="email"
              autoComplete="username"
              required
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
          </Field>

          {mode === "sign-in" ? (
            <Field label={t("Password")}>
              <Input
                type="password"
                name="password"
                autoComplete="current-password"
                required
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </Field>
          ) : null}

          <div className="flex items-center justify-between">
            <Button type="submit" variant="primary" disabled={busy}>
              {mode === "sign-in" ? t("Sign in") : t("Send a recovery token")}
            </Button>

            <Button
              onClick={() => {
                setMode(mode === "sign-in" ? "recover" : "sign-in");
                setError(null);
                setSent(false);
              }}
            >
              {mode === "sign-in" ? t("Forgot your password?") : t("Back to sign in")}
            </Button>
          </div>

          <p className="text-xs text-[var(--color-muted)]">
            {t("Have a setup or recovery token?")} <Link className="underline" to="/set-password">{t("Set a password")}</Link>.
          </p>
        </form>
      </Panel>
    </div>
  );
}
