import { useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ApiError, completeRecovery } from "../api/client";
import { Alert, BrandMark, Button, Field, Input, Panel } from "../components/ui";
import { LanguageSwitch } from "../components/LanguageSwitch";
import { t } from "../i18n";
import { TourButton } from "../components/Guide";
import { TOURS } from "../guide/content";

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
      setError(t("The two passwords do not match."));
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
        <TourButton steps={TOURS.setPassword} />
        <LanguageSwitch />
      </div>

      <Panel title={t("Set a password")} tour="set-password-form">
        {done ? (
          <div className="space-y-4">
            <Alert tone="success">
              {t("Your password is set and every other session has been signed out.")}
            </Alert>
            <Link className="text-sm underline" to="/">
              {t("Sign in")}
            </Link>
          </div>
        ) : (
          <form className="space-y-4" onSubmit={submit}>
            {error ? <Alert tone="error">{error}</Alert> : null}

            <Field label={t("Token")} hint={t("The setup or recovery token you were given.")}>
              <Input
                name="token"
                required
                value={token}
                onChange={(event) => setToken(event.target.value)}
              />
            </Field>

            <Field label={t("New password")} hint={t("At least twelve characters. Length is the only rule.")}>
              <Input
                type="password"
                name="password"
                autoComplete="new-password"
                required
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </Field>

            <Field label={t("Confirm password")}>
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
              {t("Set password")}
            </Button>
          </form>
        )}
      </Panel>
    </div>
  );
}
