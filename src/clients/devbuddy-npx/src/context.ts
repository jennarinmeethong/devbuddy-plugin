import { homedir } from "node:os";
import { join, resolve } from "node:path";
import type { CredentialStore } from "./credentials.js";
import { nativeCredentialStore } from "./credentials.js";
import { fileCredentialStore } from "./file-store.js";

export interface ClientContext {
  home: string;
  userHome: string;
  cwd: string;
  environment: NodeJS.ProcessEnv;
  out(text: string): void;
  error(text: string): void;
  store(): Promise<CredentialStore>;
}

export function contextForProcess(): ClientContext {
  const userHome = homedir();
  const home = process.env.DEVBUDDY_HOME ? resolve(process.env.DEVBUDDY_HOME) : join(userHome, ".devbuddy");
  let store: Promise<CredentialStore> | undefined;
  return {
    home, userHome, cwd: process.cwd(), environment: process.env,
    out: text => { process.stdout.write(text + "\n"); },
    error: text => { process.stderr.write(text + "\n"); },
    store() {
      return store ??= (process.env.DEVBUDDY_CREDENTIAL_STORE?.toLowerCase() === "file" || process.platform === "linux")
        ? Promise.resolve(fileCredentialStore(home)) : nativeCredentialStore();
    },
  };
}
