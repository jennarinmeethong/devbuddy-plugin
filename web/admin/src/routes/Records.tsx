import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { invoke } from "../api/client";
import type { ListRecordsArguments } from "../api/operations";
import { Badge, Empty, Panel, Select, Table, When } from "../components/ui";
import { Failure } from "../components/Failure";
import { ProjectNav } from "../components/ProjectNav";
import { m, t } from "../i18n";

type Status = NonNullable<ListRecordsArguments["statuses"]>[number];

const STATUSES: Status[] = ["Draft", "PendingApproval", "Approved", "Published", "Archived"];

const STATUS_LABELS: Record<Status, string> = {
  Draft: m("Draft"),
  PendingApproval: m("Waiting for approval"),
  Approved: m("Approved"),
  Published: m("Published"),
  Archived: m("Archived"),
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
      <ProjectNav title={t("Knowledge records")} />
      <p className="text-sm text-[var(--color-muted)]">
        {t("A new draft is written from its work item, because every record belongs to one.")}
      </p>

      <Panel
        title={t("Records")}
        actions={
          <div className="w-56">
            <Select
              aria-label={t("Status")}
              value={status}
              onChange={(event) => setStatus(event.target.value as Status | "All")}
            >
              <option value="All">{t("Every status")}</option>
              {STATUSES.map((option) => (
                <option key={option} value={option}>
                  {t(STATUS_LABELS[option])}
                </option>
              ))}
            </Select>
          </div>
        }
      >
        {records.isPending ? (
          <Empty>{t("Loading…")}</Empty>
        ) : records.isError ? (
          <Failure error={records.error} />
        ) : records.data.records.length === 0 ? (
          <Empty>
            {status === "PendingApproval"
              ? t("Nothing is waiting for approval.")
              : t("No records match that filter.")}
          </Empty>
        ) : (
          <Table head={[t("Title"), t("Kind"), t("Status"), t("Revision"), t("Updated")]}>
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
                    <Badge tone="live">{t("Published")}</Badge>
                  ) : (
                    <Badge>{t(STATUS_LABELS[record.status])}</Badge>
                  )}
                </td>
                <td className="px-2 py-2 text-xs">
                  {record.currentRevisionNumber}
                  {/*
                    Null and absent both mean "nothing published". The server omits null fields
                    rather than sending them, so a check against null alone printed the word
                    undefined at every reader of an unpublished record.
                  */}
                  {record.publishedRevisionNumber == null
                    ? " " + t("(nothing published)")
                    : record.publishedRevisionNumber !== record.currentRevisionNumber
                      ? " " + t("(published: {revision})", { revision: record.publishedRevisionNumber })
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
