export interface CredentialStore {
  readonly description: string;
  read(key: string): string | null;
  write(key: string, secret: string): void;
  delete(key: string): boolean;
}

// Literal imports only. Linux's file store belongs to the later CLI phase.
// A failed native store must never silently fall back to files.
export async function nativeCredentialStore(): Promise<CredentialStore> {
  if (process.platform === "win32") {
    const { windowsCredentialStore } = await import("./windows.js");
    return windowsCredentialStore();
  }
  if (process.platform === "darwin") {
    const { keychainCredentialStore } = await import("./keychain.js");
    return keychainCredentialStore();
  }
  throw new Error("This feasibility probe requires Windows Credential Manager or macOS Keychain.");
}
