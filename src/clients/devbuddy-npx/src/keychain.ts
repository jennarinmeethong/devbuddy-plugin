import koffi from "koffi";
import type { CredentialStore } from "./credentials.js";

let cached: CredentialStore | undefined;

export function keychainCredentialStore(): CredentialStore {
  if (cached) return cached;
  const security = koffi.load("/System/Library/Frameworks/Security.framework/Security");
  const foundation = koffi.load("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation");
  const find = security.func("int SecKeychainFindGenericPassword(void *keychain, uint32_t serviceLength, const uint8_t *service, uint32_t accountLength, const uint8_t *account, _Out_ uint32_t *length, _Out_ void **data, _Out_ void **item)");
  const add = security.func("int SecKeychainAddGenericPassword(void *keychain, uint32_t serviceLength, const uint8_t *service, uint32_t accountLength, const uint8_t *account, uint32_t length, const uint8_t *data, void *item)");
  const modify = security.func("int SecKeychainItemModifyAttributesAndData(void *item, void *attributes, uint32_t length, const uint8_t *data)");
  const remove = security.func("int SecKeychainItemDelete(void *item)");
  const free = security.func("int SecKeychainItemFreeContent(void *attributes, void *data)");
  const release = foundation.func("void CFRelease(void *item)");
  const service = Buffer.from("devbuddy", "utf8");

  function check(status: number, action: string): void {
    if (status === 0) return;
    const reason = status === -128 || status === -25293 ? " Access was denied."
      : status === -25308 ? " Unlock the login keychain and allow access." : "";
    throw new Error(`The keychain could not ${action} the token (OSStatus ${status}).${reason}`);
  }

  function lookup(key: string) {
    const account = Buffer.from(key, "utf8");
    const length = [0];
    const data = [null];
    const item = [null];
    const status = Number(find(null, service.length, service, account.length, account, length, data, item));
    if (status === -25300) return null;
    check(status, "look up");
    return { data: data[0], item: item[0], length: length[0]! };
  }

  cached = {
    description: "the macOS login keychain",
    read(key) {
      const result = lookup(key);
      if (!result) return null;
      try {
        const bytes = Buffer.from(koffi.decode(result.data, "uint8_t", result.length));
        try { return bytes.toString("utf8"); }
        finally { bytes.fill(0); }
      } finally {
        free(null, result.data);
        release(result.item);
      }
    },
    write(key, secret) {
      const result = lookup(key);
      const bytes = Buffer.from(secret, "utf8");
      try {
        if (result) {
          try { check(Number(modify(result.item, null, bytes.length, bytes)), "update"); }
          finally { free(null, result.data); release(result.item); }
        } else {
          const account = Buffer.from(key, "utf8");
          check(Number(add(null, service.length, service, account.length, account, bytes.length, bytes, null)), "store");
        }
      } finally { bytes.fill(0); }
    },
    delete(key) {
      const result = lookup(key);
      if (!result) return false;
      try { check(Number(remove(result.item)), "remove"); return true; }
      finally { free(null, result.data); release(result.item); }
    },
  };
  return cached;
}
