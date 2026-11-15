import koffi from "koffi";
import type { CredentialStore } from "./credentials.js";

// These are KnownDLLs: the Windows loader resolves the OS copies, never a checkout DLL.
// No caller can select a native library or symbol name.
let cached: CredentialStore | undefined;

export function windowsCredentialStore(): CredentialStore {
  if (cached) return cached;
  if (process.platform !== "win32" || !["x64", "arm64"].includes(process.arch)) {
    throw new Error("The Windows credential adapter requires Windows x64 or ARM64.");
  }
  const api = koffi.load("advapi32.dll");
  const kernel = koffi.load("kernel32.dll");
  const credential = koffi.struct("DevBuddyCredentialW", {
    Flags: "uint32_t",
    Type: "uint32_t",
    TargetName: "str16",
    Comment: "str16",
    LastWritten: "uint64_t",
    CredentialBlobSize: "uint32_t",
    CredentialBlob: "void *",
    Persist: "uint32_t",
    AttributeCount: "uint32_t",
    Attributes: "void *",
    TargetAlias: "str16",
    UserName: "str16",
  });
  if (koffi.sizeof(credential) !== 80 || koffi.offsetof(credential, "CredentialBlob") !== 40) {
    throw new Error("The native CREDENTIALW layout is unsupported.");
  }
  const read = api.func("int __stdcall CredReadW(str16 target, uint32_t type, uint32_t flags, _Out_ void **result)");
  const write = api.func("CredWriteW", "int", [koffi.pointer(credential), "uint32_t"]);
  const remove = api.func("int __stdcall CredDeleteW(str16 target, uint32_t type, uint32_t flags)");
  const free = api.func("void __stdcall CredFree(void *buffer)");
  const lastError = kernel.func("uint32_t __stdcall GetLastError()");

  function failure(action: string): number {
    const code = Number(lastError());
    if (code !== 1168) throw new Error(`Credential Manager could not ${action} the token (error ${code}).`);
    return code;
  }

  cached = {
    description: "Windows Credential Manager",
    read(key) {
      const pointer = [null];
      if (!read(key, 1, 0, pointer)) {
        failure("read");
        return null;
      }
      try {
        const item = koffi.decode(pointer[0], credential);
        const bytes = Buffer.from(koffi.decode(item.CredentialBlob, "uint8_t", item.CredentialBlobSize));
        try { return bytes.toString("utf8"); }
        finally { bytes.fill(0); }
      } finally { free(pointer[0]); }
    },
    write(key, secret) {
      const bytes = Buffer.from(secret, "utf8");
      try {
        if (!write({
          Flags: 0, Type: 1, TargetName: key, Comment: null, LastWritten: 0,
          CredentialBlobSize: bytes.length, CredentialBlob: bytes, Persist: 2,
          AttributeCount: 0, Attributes: null, TargetAlias: null, UserName: "devbuddy",
        }, 0)) {
          throw new Error(`Credential Manager could not store the token (error ${Number(lastError())}).`);
        }
      } finally { bytes.fill(0); }
    },
    delete(key) {
      if (remove(key, 1, 0)) return true;
      failure("remove");
      return false;
    },
  };
  return cached;
}
