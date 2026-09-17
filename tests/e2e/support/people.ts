import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import type { Api } from "./api";
import { anonymous, ApiFailure } from "./api";
import { env, password, unique } from "./env";

/**
 * The people the suite signs in as.
 *
 * One account per role, created once by the setup project in the workspace `bootstrap` made. A
 * test that needs somebody of its own — a person it will revoke, or one who will own a second
 * workspace — creates them with {@link invite}, so nothing it does can change what another test
 * sees.
 */

export type Role = "Viewer" | "Contributor" | "Reviewer" | "Administrator";

export interface Person {
  userId: string;
  email: string;
  password: string;
  displayName: string;
  role: Role;
}

export interface Cast {
  workspaceId: string;
  workspaceName: string;
  admin: Person;
  viewer: Person;
  contributor: Person;
  reviewer: Person;
}

const file = () => path.join(env.stateDirectory, "cast.json");

export function saveCast(cast: Cast): void {
  mkdirSync(env.stateDirectory, { recursive: true });
  writeFileSync(file(), JSON.stringify(cast, null, 2));
}

let cached: Cast | undefined;

export function cast(): Cast {
  cached ??= JSON.parse(readFileSync(file(), "utf8")) as Cast;
  return cached;
}

/**
 * Creates an account and sets its password the way its owner would: with the one-time setup token
 * the administrator was handed, through the same endpoint the Set password screen calls.
 */
export async function invite(
  admin: Api,
  workspaceId: string,
  role: Role,
  options: { scopedToProject?: string } = {},
): Promise<Person> {
  const label = unique(role);
  const email = `${label.replace(" ", ".").toLowerCase()}@e2e.devbuddy.test`;
  const secret = password();

  const created = await admin.invoke("create_user_account", {
    workspaceId,
    email,
    displayName: label,
    role,
    scopedToProject: options.scopedToProject ?? null,
  });

  await completeSetup(created.setupToken, secret);

  return { userId: created.userId, email, password: secret, displayName: label, role };
}

export async function completeSetup(token: string, newPassword: string): Promise<void> {
  const http = await anonymous();

  try {
    const response = await http.post("/auth/recovery/complete", { data: { token, newPassword } });

    if (!response.ok()) {
      throw await ApiFailure.from("recovery/complete", response);
    }
  } finally {
    await http.dispose();
  }
}
