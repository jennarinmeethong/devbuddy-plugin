import { expect, test } from "../support/fixtures";

/**
 * The observability overlay, end to end (Phase 13, C5). Runs only with
 * `DEVBUDDY_E2E_OBSERVABILITY=1`, where the stack exports to the shipped collector, Tempo, Loki and
 * Grafana. What matters is not only that telemetry arrives but what it carries: the tagging rule in
 * DevBuddyTelemetry says an operation name, an outcome, a channel and a scanner rule, and never a
 * workspace, project, record or user, and never a URL path.
 */
const tempo = process.env.DEVBUDDY_E2E_TEMPO_URL ?? "http://tempo:3200";
const loki = process.env.DEVBUDDY_E2E_LOKI_URL ?? "http://loki:3100";
const grafana = process.env.DEVBUDDY_E2E_GRAFANA_URL ?? "http://grafana:3000";

test.skip(process.env.DEVBUDDY_E2E_OBSERVABILITY !== "1", "Only in the observability mode.");
test.describe.configure({ timeout: 180_000 });

async function poll<T>(read: () => Promise<T | undefined>): Promise<T> {
  for (let attempt = 0; attempt < 60; attempt++) {
    const value = await read().catch(() => undefined);
    if (value !== undefined) {
      return value;
    }
    await new Promise((resolve) => setTimeout(resolve, 2000));
  }
  throw new Error("Nothing arrived in time.");
}

test("operations reach Tempo as traces that carry no identifier and no URL path", async ({ admin, people, project }) => {
  await admin.invoke("list_projects", { workspaceId: people.workspaceId });
  await admin.invoke("list_work_items", { scope: { workspaceId: project.workspaceId, projectId: project.projectId } });

  const traceIds = await poll(async () => {
    const response = await fetch(`${tempo}/api/search?limit=50&start=${Math.floor(Date.now() / 1000) - 3600}&end=${Math.floor(Date.now() / 1000) + 60}`);
    const found = (await response.json()) as { traces?: { traceID: string }[] };
    return found.traces && found.traces.length > 0 ? found.traces.map((trace) => trace.traceID) : undefined;
  });

  const bodies = await Promise.all(
    traceIds.slice(0, 20).map(async (id) => JSON.stringify(await (await fetch(`${tempo}/api/traces/${id}`)).json())),
  );
  const all = bodies.join("\n");

  expect(all).toContain("devbuddy-api");
  expect(all).not.toContain(people.workspaceId);
  expect(all).not.toContain(project.projectId);
  expect(all).not.toContain("admin@e2e.devbuddy.test");
  expect(all).not.toContain(`/workspaces/${people.workspaceId}`);
});

// Found a real defect when first run (Phase 13, C5): with the file sink on, Serilog owned the logging
// pipeline and the OpenTelemetry exporter received nothing, so no log ever reached Loki.
test("logs reach Loki", async ({ admin, people }) => {
  await admin.invoke("list_projects", { workspaceId: people.workspaceId });

  const lines = await poll(async () => {
    const query = encodeURIComponent('{service_name=~".+"}');
    const start = (Date.now() - 3600_000) * 1_000_000;
    const response = await fetch(`${loki}/loki/api/v1/query_range?query=${query}&start=${start}&limit=100`);
    const body = (await response.json()) as { data?: { result?: { values: unknown[] }[] } };
    const count = (body.data?.result ?? []).reduce((sum, stream) => sum + stream.values.length, 0);
    return count > 0 ? count : undefined;
  });

  expect(lines).toBeGreaterThan(0);
});

test("Grafana provisions the security-controls dashboard", async () => {
  const authorization = `Basic ${Buffer.from(`admin:${process.env.DEVBUDDY_E2E_GRAFANA_PASSWORD}`).toString("base64")}`;

  const titles = await poll(async () => {
    const response = await fetch(`${grafana}/api/search?query=DevBuddy`, { headers: { authorization } });
    if (!response.ok) {
      return undefined;
    }
    const found = (await response.json()) as { title: string }[];
    return found.length > 0 ? found.map((item) => item.title) : undefined;
  });

  expect(titles).toContain("DevBuddy — controls");
});
