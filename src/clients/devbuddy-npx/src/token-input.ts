import type { Readable, Writable } from "node:stream";

export const validToken = (token: unknown): token is string => typeof token === "string" && /^[A-Za-z0-9_-]{43}$/.test(token);

export class Cancelled extends Error {}

// Read only the first line. Never use readline's terminal echo for secret input.
export async function readToken(input: Readable & { isTTY?: boolean; isRaw?: boolean; setRawMode?(mode: boolean): unknown } = process.stdin, error: Writable = process.stderr): Promise<string | null> {
  const tty = input.isTTY === true && typeof input.setRawMode === "function";
  const previousRaw = input.isRaw ?? false;
  if (tty) input.setRawMode!(true);
  let typed = "";
  try {
    // The visible prompt must only appear after terminal echo has been disabled.
    if (tty) error.write("Machine token (hidden): ");
    return await new Promise<string | null>((resolve, reject) => {
      function finish(value: string | null, cancelled = false): void {
        input.off("data", data); input.off("end", end); input.off("error", failed);
        input.pause();
        if (cancelled) reject(new Cancelled("Cancelled.")); else resolve(value);
      }
      function data(chunk: Buffer | string): void {
        for (const character of chunk.toString()) {
          if (tty && character === "\x03") { finish(null, true); return; }
          if (character === "\n" || character === "\r") { finish(typed.trim()); return; }
          if (tty && character === "\x04") { finish(typed.length ? typed.trim() : null); return; }
          if (tty && (character === "\x7f" || character === "\b")) typed = typed.slice(0, -1);
          else if (character >= " ") typed += character;
          if (typed.length > 4096) { finish(null); return; }
        }
      }
      function end(): void { finish(typed.length ? typed.trim() : null); }
      function failed(): void { finish(null); }
      input.on("data", data); input.once("end", end); input.once("error", failed); input.resume();
    });
  } finally {
    typed = "";
    if (tty) { input.setRawMode!(previousRaw); error.write("\n"); }
  }
}
