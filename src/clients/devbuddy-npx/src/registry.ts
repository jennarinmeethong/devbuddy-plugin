import { chmodSync, closeSync, existsSync, mkdirSync, openSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from "node:fs";
import { dirname, join, parse, resolve, sep } from "node:path";
import { randomUUID } from "node:crypto";
import { ClientError } from "./errors.js";

export interface Checkout {
  path: string;
  server: string;
  workspace: string;
  project?: string;
  label?: string;
}

export function guid(value: unknown): string {
  if (typeof value !== "string") throw new ClientError("Expected a workspace or project GUID.");
  let text = value.trim().replace(/^(?:\{(.*)\}|\((.*)\))$/, (_match, braces: string | undefined, parentheses: string | undefined) => braces ?? parentheses ?? "").toLowerCase();
  if (/^[a-f0-9]{32}$/.test(text)) text = `${text.slice(0, 8)}-${text.slice(8, 12)}-${text.slice(12, 16)}-${text.slice(16, 20)}-${text.slice(20)}`;
  if (!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/.test(text)) {
    throw new ClientError("Expected a workspace or project GUID.");
  }
  return text;
}

export function origin(value: string): string {
  let url: URL;
  try { url = new URL(value.trim()); } catch { throw new ClientError("Expected an HTTP or HTTPS server address."); }
  if (url.username || url.password) throw new ClientError("Server addresses must not contain credentials.");
  const host = url.hostname.toLowerCase();
  const loopback = host === "localhost" || host === "[::1]" || /^127\./.test(host);
  if (url.protocol !== "https:" && !(url.protocol === "http:" && loopback)) {
    throw new ClientError("Use HTTPS; HTTP is allowed only for loopback servers.");
  }
  return url.origin.toLowerCase();
}

export const normalise = (path: string): string => resolve(path);
const comparable = (path: string): string => process.platform === "linux" ? path : path.toLowerCase();
export const same = (left: string, right: string): boolean => comparable(left) === comparable(right);
export function contains(parent: string, child: string): boolean {
  return same(parent, child) || comparable(child).startsWith(comparable(parent.endsWith(sep) ? parent : parent + sep));
}

export function checkoutRoot(folder: string, userHome: string): string {
  const wanted = normalise(folder);
  if (!existsSync(wanted) || !statSync(wanted).isDirectory()) throw new ClientError("The checkout folder does not exist.");
  let root = wanted;
  for (let candidate = wanted;; candidate = dirname(candidate)) {
    if (existsSync(join(candidate, ".git"))) { root = candidate; break; }
    if (dirname(candidate) === candidate) break;
  }
  if (same(root, parse(root).root) || contains(root, normalise(userHome))) {
    throw new ClientError("A drive root, home folder or folder above home is too broad to register.");
  }
  return root;
}

export function ownerDirectory(directory: string): void {
  mkdirSync(directory, { recursive: true, mode: 0o700 });
  if (process.platform !== "win32") chmodSync(directory, 0o700);
}

// A unique, exclusive temporary file prevents collisions, but does not provide a
// cross-client read/modify/write lock. Like .NET, simultaneous writers can lose updates.
export function atomicWrite(file: string, value: string): void {
  ownerDirectory(dirname(file));
  const temporary = `${file}.${randomUUID()}.tmp`;
  let descriptor: number | undefined;
  const buffer = Buffer.from(value, "utf8");
  try {
    descriptor = openSync(temporary, "wx", 0o600);
    writeFileSync(descriptor, buffer);
    closeSync(descriptor);
    descriptor = undefined;
    renameSync(temporary, file);
  } finally {
    buffer.fill(0);
    if (descriptor !== undefined) closeSync(descriptor);
    rmSync(temporary, { force: true });
  }
}

export class Registry {
  private constructor(readonly file: string, private checkouts: Checkout[]) {}
  static load(home: string): Registry {
    const file = join(home, "checkouts.json");
    if (!existsSync(file)) return new Registry(file, []);
    try {
      const data = JSON.parse(readFileSync(file, "utf8"));
      if (data?.version !== 1 || !Array.isArray(data.checkouts)) throw new Error();
      const entries: Checkout[] = data.checkouts.map((entry: Record<string, unknown>) => {
        if (!entry || typeof entry.path !== "string" || typeof entry.server !== "string") throw new Error();
        if (entry.label != null && typeof entry.label !== "string") throw new Error();
        return {
          path: normalise(entry.path), server: origin(entry.server), workspace: guid(entry.workspace),
          ...(entry.project == null ? {} : { project: guid(entry.project) }),
          ...(entry.label == null ? {} : { label: entry.label as string }),
        };
      });
      return new Registry(file, entries);
    } catch { throw new ClientError("The checkout registry is invalid or has an unsupported version; it was not changed."); }
  }
  get all(): readonly Checkout[] { return this.checkouts.map(entry => ({ ...entry })); }
  find(folder: string): Checkout | undefined {
    return this.all.filter(entry => contains(entry.path, normalise(folder))).sort((a, b) => b.path.length - a.path.length)[0];
  }
  usingToken(server: string, workspace: string): readonly Checkout[] {
    return this.all.filter(entry => entry.server === origin(server) && entry.workspace === guid(workspace));
  }
  put(checkout: Checkout): void {
    const entry: Checkout = { ...checkout, path: normalise(checkout.path), server: origin(checkout.server), workspace: guid(checkout.workspace) };
    if (entry.project !== undefined) entry.project = guid(entry.project);
    this.remove(entry.path);
    this.checkouts.push(entry);
  }
  remove(folder: string): boolean {
    const previous = this.checkouts.length;
    this.checkouts = this.checkouts.filter(entry => !same(entry.path, normalise(folder)));
    return previous !== this.checkouts.length;
  }
  save(): void {
    atomicWrite(this.file, JSON.stringify({ version: 1, checkouts: [...this.checkouts].sort((a, b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0) }, null, 2));
  }
}
