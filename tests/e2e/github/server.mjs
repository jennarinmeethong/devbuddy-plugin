// A stand-in for the GitHub REST API, for the GitHub source mode's end-to-end test (Phase 13, C3).
// Test-only, never shipped. It serves one repository, octo/demo, with two pull requests (one open),
// two issues (one open) plus a pull request GitHub also lists under issues, one commit and one
// comparison. It keeps every request it is sent, so the test can check what the installation asked
// for and that it presented its token.
import { createServer } from "node:http";

const head = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
const base = "0f1e2d3c4b5a69788796a5b4c3d2e1f001234567";
const when = "2026-09-20T08:00:00Z";
const author = { name: "Octo Cat", date: when };
const requests = [];

const routes = {
  "/repos/octo/demo": { default_branch: "main" },
  "/repos/octo/demo/git/ref/heads/main": { object: { sha: head } },
  [`/repos/octo/demo/commits/${head}`]: {
    sha: head,
    commit: { author },
    files: [{ filename: "src/Payments/RetryPolicy.cs" }, { filename: "docs/payments.md" }],
  },
  "/repos/octo/demo/commits/main": { sha: head, commit: { author }, files: [] },
  [`/repos/octo/demo/compare/${base}...${head}`]: {
    commits: [{ sha: head, commit: { author }, files: [] }],
    files: [{ filename: "src/Payments/RetryPolicy.cs" }],
  },
  "/repos/octo/demo/pulls": [
    { number: 1, title: "Retry with idempotency keys", state: "open", user: { login: "octo" }, body: "Adds keys.", updated_at: when, html_url: "https://github.com/octo/demo/pull/1" },
    { number: 2, title: "Old change", state: "closed", user: { login: "octo" }, body: null, updated_at: when, html_url: "https://github.com/octo/demo/pull/2" },
  ],
  "/repos/octo/demo/issues": [
    { number: 3, title: "Payments time out", state: "open", user: { login: "octo" }, body: "Seen twice.", updated_at: when, html_url: "https://github.com/octo/demo/issues/3" },
    { number: 4, title: "Closed issue", state: "closed", user: { login: "octo" }, body: null, updated_at: when, html_url: "https://github.com/octo/demo/issues/4" },
    { number: 1, title: "Retry with idempotency keys", state: "open", user: { login: "octo" }, body: "Adds keys.", updated_at: when, html_url: "https://github.com/octo/demo/pull/1", pull_request: {} },
  ],
  "/repos/octo/demo/pulls/1/comments": [{ user: { login: "reviewer" }, body: "Looks right.", created_at: when }],
  "/repos/octo/demo/pulls/2/comments": [],
};

createServer((request, response) => {
  const path = (request.url ?? "").split("?")[0];

  if (path === "/_requests") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify(requests));
    return;
  }

  requests.push({ path, authorization: request.headers.authorization ?? null });
  const body = routes[path];

  if (body === undefined) {
    response.writeHead(404, { "content-type": "application/json" });
    response.end(JSON.stringify({ message: "Not Found" }));
    return;
  }

  response.writeHead(200, { "content-type": "application/json" });
  response.end(JSON.stringify(body));
}).listen(8080);
