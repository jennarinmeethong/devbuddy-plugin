import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { invoke } from "../api/client";
import type { ListRecordsArguments } from "../api/operations";
import { Badge, Empty, Panel, Select, Table, When } from "../components/ui";
import { Failure } from "../components/Failure";

type Status = NonNullable<ListRecordsArguments["statuses"]>[number];

const STATUSES: Status[] = ["Draft", "PendingApproval", "Approved", "Published", "Archived"];

const STATUS_LABELS: Record<Status, string> = {
  Draft: "Draft",
  PendingApproval: "Waiting for approval",
  Approved: "Approved",
  Published: "Published",
  Archived: "Archived",
};

/**
 * Knowledge records in a project, and the review queue.
 *
 * The queue defaults to what is waiting for approval, because that is the only view with a person
 * blocked behind it. Drafts and published records are the same list with a different filter — one
 * screen rather than two that could disagree about what a status means.
 */
export function Records() {
  const { workspaceId, projectId } = useParams();
  const [status, setStatus] = useState<Status | "All">("PendingApproval");

  const scope = { workspaceId: workspaceId!, projectId: projectId! };

  const records = useQuery({
    queryKey: ["records", workspaceId, projectId, status],
    queryFn: () =>
      invoke("list_records", {
        scope,
        statuses: status === "All" ? null : [status],
      }),
    enabled: Boolean(workspaceId && projectId),
  });

  return (
    <>
      <div className="flex items-center justify-between">
        <h1 className="text-lg font-semibold">Knowledge records</h1>
        <Link className="text-sm underline" to="..">
          Work items
        </Link>
      </div>

      <Panel
        title="Records"
        actions={
          <div className="w-56">
            <Select
              aria-label="Status"
              value={status}
              onChange={(event) => setStatus(event.target.value as Status | "All")}
            >
              <option value="All">Every status</option>
              {STATUSES.map((option) => (
                <option key={option} value={option}>
                  {STATUS_LABELS[option]}
                </option>
              ))}
            </Select>
          </div>
        }
      >
        {records.isPending ? (
          <Empty>Loading…</Empty>
        ) : records.isError ? (
          <Failure error={records.error} />
        ) : records.data.records.length === 0 ? (
          <Empty>
            {status === "PendingApproval"
              ? "Nothing is waiting for approval."
              : "No records match that filter."}
          </Empty>
        ) : (
          <Table head={["Title", "Kind", "Status", "Revision", "Updated"]}>
            {records.data.records.map((record) => (
              <tr key={record.recordId} className="border-b border-[var(--color-line)] last:border-0">
                <td className="px-2 py-2">
                  <Link className="underline" to={record.recordId}>
                    {record.title}
                  </Link>
                </td>
                <td className="px-2 py-2 text-xs text-[var(--color-muted)]">{record.kind}</td>
                <td className="px-2 py-2">
                  {record.status === "Published" ? (
                    <Badge tone="live">Published</Badge>
                  ) : (
                    <Badge>{STATUS_LABELS[record.status]}</Badge>
                  )}
                </td>
                <td className="px-2 py-2 text-xs">
                  {record.currentRevisionNumber}
                  {record.publishedRevisionNumber !== null &&
                  record.publishedRevisionNumber !== record.currentRevisionNumber
                    ? ` (published: ${record.publishedRevisionNumber})`
                    : ""}
                </td>
                <td className="px-2 py-2 text-xs">
                  <When value={record.updatedAt} />
                </td>
              </tr>
            ))}
          </Table>
        )}
      </Panel>
    </>
  );
}
