import { expect, test } from "bun:test";
import { readdirSync, readFileSync } from "node:fs";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { violations } from "../tools/source-guard.ts";

const source = fileURLToPath(new URL("../src/", import.meta.url));

function productFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? productFiles(path) : /\.(?:[cm]?[jt]s)$/.test(entry.name) ? [path] : [];
  });
}

test("guard scans every product TypeScript file, including both native adapters", () => {
  const files = productFiles(source);
  expect(files.map(file => relative(source, file))).toContain("windows.ts");
  expect(files.map(file => relative(source, file))).toContain("keychain.ts");
  expect(files.length).toBeGreaterThanOrEqual(5);
  for (const file of files) expect(violations(readFileSync(file, "utf8"), relative(source, file))).toEqual([]);
});

test("guard rejects execution and caller-selected native libraries", () => {
  const mutations = [
    'import { spawn as run } from "node:child_process"; run("git");',
    'import { Script } from "node:vm";',
    'Bun.spawn(["git"]);',
    'eval("payload");',
    'new Function("payload");',
    'import(userPath);',
    'koffi.load(userPath);',
    'koffi.load("./advapi32.dll");',
    'process.dlopen(module, userPath);',
    'const run = process.getBuiltinModule("child_process").spawn; run("git");',
    'import { createRequire as loader } from "node:module";',
    'const run = globalThis["eval"]; run("payload");',
    'const run = Function; run("payload")();',
    'const load = koffi.load; load(userPath);',
    'import ffi from "koffi"; ffi.load(userPath);',
    'import("koffi/indirect");',
    'import "unreviewed-package";',
    'const run = Bun.$; run`git status`;',
    'new Deno.Command("git");',
    'require(userPath);',
    'export * from "node:child_process";',
  ];
  for (const mutation of mutations) expect(violations(mutation, "windows.ts").length).toBeGreaterThan(0);
  expect(violations('import koffi from "koffi";', "other.ts").length).toBeGreaterThan(0);
});

test("literal imports and approved adapter libraries are accepted", () => {
  expect(violations('import("./windows.js");', "credentials.ts")).toEqual([]);
  expect(violations('koffi.load("advapi32.dll");', "windows.ts")).toEqual([]);
});
