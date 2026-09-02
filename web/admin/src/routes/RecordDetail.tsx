import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { grants, useWorkspace } from "../api/session";
import { Alert, Badge, Button, Empty, Field, Hash, Panel, TextArea, When } from "../components/ui";
import { Failure } from "../components/Failure";

/**
 * One record: its body, its history, and the approval screen.
 *
 * The approval screen is the reason this page is careful. It shows the exact revision being
 * approved and its content hash, and it submits that hash. There is no way from here to approve
 * "the latest": if somebody revises the record while a reviewer is reading it, the hash they read
 * no longer matches and the server rejects the approval. That is control SB-23, and the UI is
 * built so a reviewer cannot route around it by accident.
 */
export function RecordDetail() {
  const { workspaceId, projectId, recordId } = useParams();
  const access = useWorkspace(workspaceId);
  const scope = { workspaceId: workspaceId!, projectId: projectId! };

  const record = useQuery({
    queryKey: ["record", workspaceId, projectId, recordId],
    queryFn: () => invoke("get_record", { scope, recordId: recordId! }),
    enabled: Boolean(workspaceId && projectId && recordId),
  });

  const history = useQuery({
    queryKey: ["history", workspaceId, projectId, recordId],
    queryFn: () => invoke("view_record_history", { scope, recordId: recordId! }),
    enabled: Boolean(workspaceId && projectId && recordId),
  });

  if (history.isError) {
    return <Failure error={history.error} />;
  }

  const latest = history.data?.revisions.reduce(
    (newest, revision) => (newest && newest.number > revision.number ? newest : revision),
    history.data.revisions[0],
  );

  return (
    <>
      <div className="flex items-center justify-between">
        <h1 className="text-lg font-semibold">{record.data?.title ?? "Record"}</h1>
        <Link className="text-sm underline" to="..">
          Back to records
        </Link>
      </div>

      <Panel
        title="Current content"
        actions={history.data ? <Badge>{history.data.status}</Badge> : null}
      >
        {record.isPending ? (
          <Empty>Loading…</Empty>
        ) : record.isError ? (
          <Failure error={record.error} />
        ) : (
          <article className="space-y-3">
            <p className="text-xs text-[var(--color-muted)]">
              Revision {record.data.revisionNumber}
              {record.data.provenance.isAiGenerated ? " · drafted by AI" : ""} · from{" "}
              {record.data.provenance.sourceLocator} · recorded by {record.data.provenance.author}
            </p>
            <pre className="overflow-x-auto whitespace-pre-wrap rounded bg-neutral-50 p-3 text-sm">
              {record.data.body}
            </pre>
          </article>
        )}
      </Panel>

      {history.data && latest && grants(access, "ReviewRecord") && history.data.status === "PendingApproval" ? (
        <Approval scope={scope} recordId={recordId!} revision={latest} />
      ) : null}

      {history.data && grants(access, "PublishRecord") && history.data.status === "Approved" ? (
        <Publish scope={scope} recordId={recordId!} />
      ) : null}

      <Panel title="History">
        {history.isPending ? (
          <Empty>Loading…</Empty>
        ) : (
          <ol className="space-y-3">
            {[...history.data.revisions]
              .sort((left, right) => right.number - left.number)
              .map((revision) => (
                <li
                  key={revision.number}
                  className="rounded border border-[var(--color-line)] p-3 text-sm"
                >
                  <div className="flex items-center justify-between">
                    <span className="font-medium">
                      Revision {revision.number} — {revision.title}
                    </span>
                    {revision.isPublished ? <Badge tone="live">Published</Badge> : null}
                  </div>

                  <p className="mt-1 text-xs text-[var(--color-muted)]">
                    <When value={revision.createdAt} /> · {revision.provenance.sourceKind} ·{" "}
                    {revision.provenance.author}
                    {revision.provenance.evidenceCount > 0
                      ? ` · ${revision.provenance.evidenceCount} evidence item(s)`
                      : ""}
                  </p>

                  <p className="mt-1">
                    <Hash value={revision.contentHash} />
                  </p>

                  {revision.approval ? (
                    <p className="mt-2 text-xs">
                      Approved <When value={revision.approval.approvedAt} /> by{" "}
                      <span className="font-mono">{revision.approval.approverId}</span>
                      {revision.approval.approverWasDraftCreator
                        ? " — who also wrote the draft"
                        : ""}
                    </p>
                  ) : (
                    <p className="mt-2 text-xs text-[var(--color-muted)]">No approval covers this revision.</p>
                  )}
                </li>
              ))}
          </ol>
        )}
      </Panel>
    </>
  );
}

interface Revision {
  number: number;
  contentHash: string;
  title: string;
}

function Approval({
  scope,
  recordId,
  revision,
}: {
  scope: { workspaceId: string; projectId: string };
  recordId: string;
  revision: Revision;
}) {
  const queries = useQueryClient();
  const [reason, setReason] = useState("");

  async function refresh() {
    await queries.invalidateQueries({ queryKey: ["record", scope.workspaceId, scope.projectId, recordId] });
    await queries.invalidateQueries({ queryKey: ["history", scope.workspaceId, scope.projectId, recordId] });
    await queries.invalidateQueries({ queryKey: ["records", scope.workspaceId, scope.projectId] });
  }

  const approve = useMutation({
    // The hash of the revision on screen, never a request for "the latest". If the record moved
    // while this page was open, the server rejects this and the reviewer reads the new one.
    mutationFn: () =>
      invoke("approve_record", { scope, recordId, approvedContentHash: revision.contentHash }),
    onSuccess: refresh,
  });

  const correct = useMutation({
    mutationFn: () => invoke("request_correction", { scope, recordId, reason }),
    onSuccess: async () => {
      setReason("");
      await refresh();
    },
  });

  return (
    <Panel title="Approve or send back">
      <div className="space-y-4">
        <Alert>
          You are approving <strong>revision {revision.number}</strong>, and nothing else. The
          approval binds to this exact content:
          <span className="mt-1 block">
            <Hash value={revision.contentHash} />
          </span>
        </Alert>

        <div className="flex gap-2">
          <Button variant="primary" disabled={approve.isPending} onClick={() => approve.mutate()}>
            Approve revision {revision.number}
          </Button>
        </div>

        {approve.isError ? <Failure error={approve.error} /> : null}

        <form
          className="space-y-2 border-t border-[var(--color-line)] pt-4"
          onSubmit={(event) => {
            event.preventDefault();
            correct.mutate();
          }}
        >
          <Field label="Or send it back" hint="The reason is kept on the record.">
            <TextArea
              required
              rows={2}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
            />
          </Field>

          <Button type="submit" disabled={correct.isPending}>
            Request a correction
          </Button>

          {correct.isError ? <Failure error={correct.error} /> : null}
        </form>
      </div>
    </Panel>
  );
}

function Publish({
  scope,
  recordId,
}: {
  scope: { workspaceId: string; projectId: string };
  recordId: string;
}) {
  const queries = useQueryClient();

  const publish = useMutation({
    mutationFn: () => invoke("publish_record", { scope, recordId }),
    onSuccess: async () => {
      await queries.invalidateQueries({ queryKey: ["history", scope.workspaceId, scope.projectId, recordId] });
      await queries.invalidateQueries({ queryKey: ["records", scope.workspaceId, scope.projectId] });
    },
  });

  return (
    <Panel title="Publish">
      <div className="space-y-3">
        <p className="text-sm text-[var(--color-muted)]">
          Publishing makes the approved revision the one readers see. An approval that no longer
          covers the current content is refused.
        </p>
        <Button variant="primary" disabled={publish.isPending} onClick={() => publish.mutate()}>
          Publish
        </Button>
        {publish.isError ? <Failure error={publish.error} /> : null}
      </div>
    </Panel>
  );
}
