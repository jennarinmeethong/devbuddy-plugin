// A stand-in embedding server for the end-to-end suite (Phase 13, C1). Test-only, never shipped.
//
// It speaks the OpenAI-compatible POST /v1/embeddings shape the real adapter sends, and answers with
// a deterministic bag-of-words vector, so similar text really is closer and a search can be asserted
// on. Every text it receives is kept, and GET /received returns them: the suite asserts on what
// actually left the installation, which is the point of SB-34.
import { createServer } from "node:http";

const dimensions = Number(process.env.EMBEDDER_DIMENSIONS ?? 64);
const received = [];

function vectorOf(text) {
  const vector = new Array(dimensions).fill(0);
  for (const word of text.toLowerCase().match(/[\p{L}\p{N}]+/gu) ?? []) {
    let hash = 2166136261;
    for (const character of word) {
      hash = Math.imul(hash ^ character.codePointAt(0), 16777619) >>> 0;
    }
    vector[hash % dimensions] += 1;
  }
  const length = Math.hypot(...vector) || 1;
  return vector.map((value) => value / length);
}

createServer((request, response) => {
  if (request.method === "GET" && request.url === "/received") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify(received));
    return;
  }

  if (request.method !== "POST" || !request.url?.endsWith("/embeddings")) {
    response.writeHead(404).end();
    return;
  }

  let body = "";
  request.on("data", (chunk) => (body += chunk));
  request.on("end", () => {
    const { input } = JSON.parse(body);
    const texts = Array.isArray(input) ? input : [input];
    received.push(...texts);
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify({ data: texts.map((text, index) => ({ index, embedding: vectorOf(text) })) }));
  });
}).listen(8080);
