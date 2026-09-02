import { useState } from "react";
import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { Badge, Empty, Field, Input, Panel, Select, Table, When } from "../components/ui";
import { Failure } from "../components/Failure";

/**
 * The audit trail for one project.
 *
 * Entries say that something happened, to what, by whom, and whether it was allowed. They never
 * carry the content that was touched — that is control SB-19, enforced in the domain rather than
 * trusted here — so this screen shows identifiers and states and nothing that would put a record
 * body into a second store.
 */
export function Audit() {
  const { workspaceId } = useParams();
  const [projectId, setProjectId] = useState("");
  const [days, setDays] = useState(7);

  const projects = useQuery({
    queryKey: ["projects", workspaceId],
    queryFn: () => invoke("list_projects", { workspaceId: workspaceId! }),
    enabled: Boolean(workspaceId),
  });

  const until = new Date();
  const from = new Date(until.getTime() - days * 24 * 60 * 60 * 1000);

  const entries = useQuery({
    queryKey: ["audit", workspaceId, projectId, days],
    queryFn: () =>
      invoke("read_audit_history", {
        scope: { workspaceId: workspaceId!, projectId },
        occurredFrom: from.toISOString(),
        occurredUntil: until.toISOString(),
      }),
    enabled: Boolean(workspaceId && projectId),
  });

  return (
    <Panel title="Audit history">
      <form className="mb-4 grid gap-3 sm:grid-cols-2">
        <Field label="Project">
          <Select value={projectId} onChange={(event) => setProjectId(event.target.value)}>
            <option value="">Choose a project…</option>
            {(projects.data?.projects ?? []).map((project) => (
              <option key={project.projectId} value={project.projectId}>
                {project.name}
              </option>
            ))}
          </Select>
        </Field>

        <Field label="Days back">
          <Input
            type="number"
            min={1}
            max={365}
            value={days}
            onChange={(event) => setDays(Number(event.target.value) || 1)}
          />
        </Field>
      </form>

      {!projectId ? (
        <Empty>Choose a project to read its audit history.</Empty>
      ) : entries.isPending ? (
        <Empty>Loading…</Empty>
      ) : entries.isError ? (
        <Failure error={entries.error} />
      ) : entries.data.entries.length === 0 ? (
        <Empty>Nothing in that window.</Empty>
      ) : (
        <Table head={["When", "Action", "Outcome", "Actor", "Resource", "Detail"]}>
          {entries.data.entries.map((entry) => (
            <tr key={entry.id} className="border-b border-[var(--color-line)] last:border-0">
              <td className="px-2 py-2 text-xs">
                <When value={entry.occurredAt} />
              </td>
              <td className="px-2 py-2">{entry.action}</td>
              <td className="px-2 py-2">
                {entry.outcome === "Succeeded" ? (
                  <Badge tone="live">Succeeded</Badge>
                ) : (
                  <Badge>{entry.outcome}</Badge>
                )}
              </td>
              <td className="px-2 py-2 font-mono text-xs">{entry.actorId}</td>
              <td className="px-2 py-2 font-mono text-xs">{entry.resourceReference}</td>
              <td className="px-2 py-2 text-xs text-[var(--color-muted)]">
                {entry.details
                  ? Object.entries(entry.details)
                      .map(([key, value]) => `${key}=${value}`)
                      .join(" ")
                  : "—"}
              </td>
            </tr>
          ))}
        </Table>
      )}
    </Panel>
  );
}
