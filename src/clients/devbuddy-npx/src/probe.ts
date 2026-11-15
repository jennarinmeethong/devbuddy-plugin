import { createHash, randomUUID } from "node:crypto";
import { readFileSync } from "node:fs";
import { nativeCredentialStore } from "./credentials.js";

// The prototype touches only its own random test keys; never registered workspaces.
export function probeKey(id: string): string {
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(id)) {
    throw new Error("A lowercase UUID is required for an isolated probe key.");
  }
  return `devbuddy:test/npx/${id}`;
}

export interface NativeFileEvidence {
  path: string;
  sha256: string | null;
  hashStatus: "hashed" | "unavailable-system-image";
}

export function inventoryFiles(
  files: string[],
  platform: NodeJS.Platform = process.platform,
  read: (path: string) => Uint8Array = path => readFileSync(path),
): NativeFileEvidence[] {
  return [...new Set(files)].map(path => {
    try {
      return { path, sha256: createHash("sha256").update(read(path)).digest("hex"), hashStatus: "hashed" };
    } catch (error) {
      // macOS can report loaded system images whose files are not individually present.
      // Record unavailable evidence explicitly; never skip a missing runtime or npm module.
      const systemImage = !/\.node$/i.test(path) && (path.startsWith("/System/Library/Frameworks/")
        || path.startsWith("/System/Library/PrivateFrameworks/")
        || (path.startsWith("/usr/lib/") && path.endsWith(".dylib")));
      if (platform === "darwin" && systemImage && (error as NodeJS.ErrnoException).code === "ENOENT") {
        return { path, sha256: null, hashStatus: "unavailable-system-image" };
      }
      throw error;
    }
  });
}

export function nativeInventory(): NativeFileEvidence[] {
  const report = process.report.getReport() as { sharedObjects?: string[] };
  const files = [...new Set([process.execPath, ...(report.sharedObjects ?? [])])];
  return inventoryFiles(files);
}

export async function runProbe(args: string[]): Promise<object> {
  if (Number(process.versions.node.split(".")[0]) !== 24) throw new Error("Run the feasibility probe with Node.js 24.");
  const [stage = "roundtrip", id = randomUUID()] = args;
  if (args.length > 2 || !["roundtrip", "read-dotnet", "verify-node", "cleanup"].includes(stage)) {
    throw new Error("Use roundtrip, read-dotnet, verify-node or cleanup, optionally followed by a probe UUID.");
  }
  const key = probeKey(id);
  const store = await nativeCredentialStore();
  const expect = (condition: boolean) => { if (!condition) throw new Error("Credential compatibility assertion failed; no token value is printed."); };
  if (stage === "roundtrip") {
    expect(store.read(key) === null);
    try {
      store.write(key, "A".repeat(43));
      expect(store.read(key) === "A".repeat(43));
      store.write(key, "B".repeat(43));
      expect(store.read(key) === "B".repeat(43));
      expect(store.delete(key));
      expect(store.read(key) === null);
      expect(!store.delete(key));
    } finally { store.delete(key); }
  } else if (stage === "read-dotnet") {
    expect(store.read(key) === "D".repeat(43));
    store.write(key, "N".repeat(43));
  } else if (stage === "verify-node") {
    expect(store.read(key) === "V".repeat(43));
  } else {
    store.delete(key);
  }
  return {
    stage, passed: true, platform: process.platform, architecture: process.arch,
    node: process.version, store: store.description, nativeFiles: nativeInventory(),
    // SAC state and signatures are collected externally; a successful load is not SAC evidence.
    sacCompatible: "UNVERIFIED",
  };
}
