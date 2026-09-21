import { anonymous, Api } from "../support/api";
import { password } from "../support/env";
import { expect, test } from "../support/fixtures";
import { invite } from "../support/people";

/**
 * Mail, delivered (Phase 13, C4). The stack sends over SMTP to Mailpit here, so what an installation
 * with a mail server actually sends is read back and used: the invitation's setup token, and a
 * recovery that works end to end without an administrator in the loop.
 */
const mailpit = process.env.DEVBUDDY_E2E_MAILPIT_URL ?? "http://mailpit:8025";

async function latestTo(address: string): Promise<{ subject: string; text: string }> {
  for (let attempt = 0; attempt < 30; attempt++) {
    const search = await fetch(`${mailpit}/api/v1/search?query=${encodeURIComponent(`to:"${address}"`)}`);
    const found = (await search.json()) as { messages: { ID: string; Subject: string }[] };

    if (found.messages.length > 0) {
      const message = await fetch(`${mailpit}/api/v1/message/${found.messages[0].ID}`);
      const body = (await message.json()) as { Subject: string; Text: string };
      return { subject: body.Subject, text: body.Text };
    }

    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  throw new Error(`No mail reached ${address}.`);
}

test("an invitation is delivered to the new person's address with the setup token", async ({ admin, people }) => {
  const person = await invite(admin, people.workspaceId, "Viewer");

  const mail = await latestTo(person.email);

  expect(mail.subject).toBe("Your DevBuddy account is ready");
  expect(mail.text).toMatch(/Setup token: \S{20,}/);
});

test("password recovery works end to end through the delivered mail", async ({ admin, people }) => {
  const person = await invite(admin, people.workspaceId, "Viewer");
  const http = await anonymous();

  try {
    expect((await http.post("/auth/recovery/begin", { data: { email: person.email } })).status()).toBe(202);

    let token = "";
    for (let attempt = 0; attempt < 30 && !token; attempt++) {
      const mail = await latestTo(person.email);
      token = /Use this token to reset your password: (\S+)/.exec(mail.text)?.[1] ?? "";
      if (!token) {
        await new Promise((resolve) => setTimeout(resolve, 500));
      }
    }
    expect(token.length).toBeGreaterThan(20);

    const chosen = password();
    expect((await http.post("/auth/recovery/complete", { data: { token, newPassword: chosen } })).status()).toBe(204);

    const api = await Api.signIn(person.email, chosen);
    await api.dispose();
  } finally {
    await http.dispose();
  }
});

test("recovery for an address nobody holds sends nothing", async () => {
  const address = `nobody.${Date.now().toString(36)}@e2e.devbuddy.test`;
  const http = await anonymous();

  try {
    expect((await http.post("/auth/recovery/begin", { data: { email: address } })).status()).toBe(202);
  } finally {
    await http.dispose();
  }

  await new Promise((resolve) => setTimeout(resolve, 3000));
  const search = await fetch(`${mailpit}/api/v1/search?query=${encodeURIComponent(`to:"${address}"`)}`);
  expect(((await search.json()) as { messages: unknown[] }).messages).toHaveLength(0);
});
