import { createHash } from "node:crypto";
import { existsSync, readFileSync, unlinkSync } from "node:fs";
import { join } from "node:path";
import type { CredentialStore } from "./credentials.js";
import { atomicWrite, guid, origin, ownerDirectory } from "./registry.js";

export const credentialKey = (server: string, workspace: string): string => `devbuddy:${origin(server)}/${guid(workspace)}`;

export function fileCredentialStore(home: string): CredentialStore {
  const directory = join(home, "tokens");
  const pathFor = (key: string): string => join(directory, createHash("sha256").update(key, "utf8").digest("hex"));
  return {
    description: `a file readable only by you, in ${directory}`,
    read(key) { const file = pathFor(key); return existsSync(file) ? readFileSync(file, "utf8").trim() : null; },
    write(key, secret) { ownerDirectory(home); atomicWrite(pathFor(key), secret); },
    delete(key) {
      try { unlinkSync(pathFor(key)); return true; }
      catch (error) { if ((error as NodeJS.ErrnoException).code === "ENOENT") return false; throw error; }
    },
  };
}
