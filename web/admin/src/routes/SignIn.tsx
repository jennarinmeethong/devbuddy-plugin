import { useState } from "react";
import { Link } from "react-router-dom";
import { ApiError, beginRecovery, signIn } from "../api/client";
import { useSession } from "../api/session";
import { Alert, Button, Field, Input, Panel } from "../components/ui";

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
          : "The server could not be reached.",
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mx-auto max-w-md p-8">
      <h1 className="mb-6 text-lg font-semibold">DevBuddy</h1>

      <Panel title={mode === "sign-in" ? "Sign in" : "Recover your account"}>
        <form className="space-y-4" onSubmit={submit}>
          {error ? <Alert tone="error">{error}</Alert> : null}

          {sent ? (
            <Alert tone="success">
              If that address has an account, a recovery token has been issued. It is delivered out
              of band — ask whoever runs this installation for it, then use the link below.
            </Alert>
          ) : null}

          <Field label="Email">
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
            <Field label="Password">
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
              {mode === "sign-in" ? "Sign in" : "Send a recovery token"}
            </Button>

            <Button
              onClick={() => {
                setMode(mode === "sign-in" ? "recover" : "sign-in");
                setError(null);
                setSent(false);
              }}
            >
              {mode === "sign-in" ? "Forgot your password?" : "Back to sign in"}
            </Button>
          </div>

          <p className="text-xs text-[var(--color-muted)]">
            Have a setup or recovery token? <Link className="underline" to="/set-password">Set a password</Link>.
          </p>
        </form>
      </Panel>
    </div>
  );
}
