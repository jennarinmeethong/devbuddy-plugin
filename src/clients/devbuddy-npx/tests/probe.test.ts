import { expect, test } from "bun:test";
import { inventoryFiles, probeKey } from "../src/probe.ts";

test("probe can address only isolated test keys, never a workspace credential", () => {
  expect(probeKey("12345678-1234-1234-1234-123456789abc")).toBe("devbuddy:test/npx/12345678-1234-1234-1234-123456789abc");
  for (const input of ["", "devbuddy:https://example.test/workspace", "../tokens", "12345678-1234-1234-1234-123456789ABC"]) {
    expect(() => probeKey(input)).toThrow("lowercase UUID");
  }
});

test("inventory hashes readable files and deduplicates paths", () => {
  const result = inventoryFiles(["node", "node"], "darwin", () => Buffer.from("abc"));
  expect(result).toEqual([{ path: "node", sha256: "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hashStatus: "hashed" }]);
});

test("missing macOS system images are explicitly unavailable, not falsely hashed", () => {
  const missing = () => { throw Object.assign(new Error("missing"), { code: "ENOENT" }); };
  for (const path of [
    "/System/Library/Frameworks/CoreFoundation.framework/Versions/A/CoreFoundation",
    "/System/Library/PrivateFrameworks/CoreServicesInternal.framework/Versions/A/CoreServicesInternal",
    "/usr/lib/libSystem.B.dylib",
  ]) {
    expect(inventoryFiles([path], "darwin", missing)).toEqual([{ path, sha256: null, hashStatus: "unavailable-system-image" }]);
  }
});

test("missing runtime or npm binaries and non-macOS paths still fail evidence collection", () => {
  const missing = () => { throw Object.assign(new Error("missing"), { code: "ENOENT" }); };
  for (const path of ["/tmp/koffi.node", "/usr/local/bin/node", "/Users/jm/module.node",
    "/usr/lib/node_modules/koffi.node", "/usr/lib/node", "/System/Library/Frameworks/koffi.node"]) {
    expect(() => inventoryFiles([path], "darwin", missing)).toThrow("missing");
  }
  expect(() => inventoryFiles(["/usr/lib/system.dll"], "win32", missing)).toThrow("missing");
});

test("permission failures are never treated as absent system images", () => {
  const denied = () => { throw Object.assign(new Error("denied"), { code: "EACCES" }); };
  expect(() => inventoryFiles(["/System/Library/Frameworks/CoreFoundation"], "darwin", denied)).toThrow("denied");
});
