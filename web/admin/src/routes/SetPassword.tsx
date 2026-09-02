import { useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ApiError, completeRecovery } from "../api/client";
import { Alert, Button, Field, Input, Panel } from "../components/ui";

/**
 * Redeems a setup or recovery token and sets a password.
 *
 * One screen for both, because they are the same thing: a single-use, time-boxed token that lets
 * the person who holds it choose a password. A newly created account has no credential at all
 * until this runs, which is what makes "an administrator created your account" different from
 * "an administrator knows your password".
 */
export function SetPassword() {
  const [params] = useSearchParams();
  const [token, setToken] = useState(params.get("token") ?? "");
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);

    if (password !== confirmation) {
      setError("The two passwords do not match.");
      return;
    }

    setBusy(true);

    try {
      await completeRecovery(token, password);
      setDone(true);
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

      <Panel title="Set a password">
        {done ? (
          <div className="space-y-4">
            <Alert tone="success">
              Your password is set and every other session has been signed out.
            </Alert>
            <Link className="text-sm underline" to="/">
              Sign in
            </Link>
          </div>
        ) : (
          <form className="space-y-4" onSubmit={submit}>
            {error ? <Alert tone="error">{error}</Alert> : null}

            <Field label="Token" hint="The setup or recovery token you were given.">
              <Input
                name="token"
                required
                value={token}
                onChange={(event) => setToken(event.target.value)}
              />
            </Field>

            <Field label="New password" hint="At least twelve characters. Length is the only rule.">
              <Input
                type="password"
                name="password"
                autoComplete="new-password"
                required
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </Field>

            <Field label="Confirm password">
              <Input
                type="password"
                name="confirmation"
                autoComplete="new-password"
                required
                value={confirmation}
                onChange={(event) => setConfirmation(event.target.value)}
              />
            </Field>

            <Button type="submit" variant="primary" disabled={busy}>
              Set password
            </Button>
          </form>
        )}
      </Panel>
    </div>
  );
}
