import { Fragment, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { invoke } from "../api/client";
import type { GetRecordResult, ViewRecordHistoryResult } from "../api/operations";
import { grants, useSession, useWorkspace } from "../api/session";
import { Alert, Badge, Button, Empty, Field, Hash, Input, Panel, TextArea, When } from "../components/ui";
import { Failure } from "../components/Failure";

/**
 * One record: its body, its history, and every lifecycle step a person takes on it.
 *
 * Draft → revise → submit for approval → approve or send back → publish, with archive available
 * until the record is archived. Each step is offered only in the state the domain accepts it in and
 * only to a caller holding the permission the server will check; neither is a control, and both
 * are re-checked server-side.
 *
 * The approval screen is the reason this page is careful. It shows the exact revision being
 * approved and its content hash, and it submits that hash. There is no way from here to approve
 * "the latest": if somebody revises the record while a reviewer is reading it, the hash they read
 * no longer matches and the server rejects the approval. That is control SB-23, and the UI is
 * built so a reviewer cannot route around it by accident. The front matter is part of that hash,
 * so it is shown with the body rather than left for the reviewer to take on trust.
 */
export function RecordDetail() {
  const { workspaceId, projectId, recordId } = useParams();
  const access = useWorkspace(workspaceId);
  const scope = { workspaceId: workspaceId!, projectId: projectId! };

  const history = useQuery({
    queryKey: ["history", workspaceId, projectId, recordId],
    queryFn: () => invoke("view_record_history", { scope, recordId: recordId! }),
    enabled: Boolean(workspaceId && projectId && recordId),
  });

  const latest = history.data?.revisions.reduce(
    (newest, revision) => (newest && newest.number > revision.number ? newest : revision),
    history.data.revisions[0],
  );

  const published = history.data?.revisions.find((revision) => revision.isPublished);

  // Every revision this page shows is asked for by number. With no number, get_record answers the
  // published revision and nothing else, and a record never published answers not found (SB-26), so
  // neither panel may lean on that default. The first panel is what readers see when something is
  // published, and the newest revision when nothing is.
  const shown = published ?? latest;

  const record = useQuery({
    queryKey: ["record", workspaceId, projectId, recordId, shown?.number],
    queryFn: () => invoke("get_record", { scope, recordId: recordId!, revisionNumber: shown!.number }),
    enabled: Boolean(workspaceId && projectId && recordId && shown),
  });

  // A published record that has been revised since: the page would otherwise show the published
  // body above an approval that binds the newer revision's hash, so a reviewer would approve
  // content they were never shown.
  const unpublished = published && latest && latest.number !== published.number ? latest : undefined;

  const underWork = useQuery({
    queryKey: ["record", workspaceId, projectId, recordId, unpublished?.number],
    queryFn: () => invoke("get_record", { scope, recordId: recordId!, revisionNumber: unpublished!.number }),
    enabled: Boolean(unpublished),
  });

  if (history.isError) {
    return <Failure error={history.error} />;
  }

  const status = history.data?.status;
  const corrections = history.data?.corrections ?? [];

  return (
    <>
      <div className="flex items-center justify-between">
        <h1 className="text-lg font-semibold">{record.data?.title ?? "Record"}</h1>
        <Link className="text-sm underline" to="..">
          Back to records
        </Link>
      </div>

      <Panel
        title={unpublished ? "Published content" : "Current content"}
        actions={status ? <Badge>{status}</Badge> : null}
      >
        {record.isPending ? (
          <Empty>Loading…</Empty>
        ) : record.isError ? (
          <Failure error={record.error} />
        ) : (
          <Content record={record.data} />
        )}
      </Panel>

      {unpublished ? (
        <Panel title={`Revision ${unpublished.number} — not published`}>
          {underWork.isPending ? (
            <Empty>Loading…</Empty>
          ) : underWork.isError ? (
            <Failure error={underWork.error} />
          ) : (
            <Content record={underWork.data} />
          )}
        </Panel>
      ) : null}

      {latest && grants(access, "CreateDraft") && status === "Draft" ? (
        <Revise
          scope={scope}
          recordId={recordId!}
          revisionNumber={latest.number}
          sentBack={corrections.filter((correction) => correction.targetRevisionNumber === latest.number)}
        />
      ) : null}

      {latest && grants(access, "CreateDraft") && status === "Draft" ? (
        <Submit scope={scope} recordId={recordId!} revision={latest} />
      ) : null}

      {latest && grants(access, "ReviewRecord") && status === "PendingApproval" ? (
        <Approval scope={scope} recordId={recordId!} revision={latest} />
      ) : null}

      {grants(access, "PublishRecord") && status === "Approved" ? (
        <Publish scope={scope} recordId={recordId!} />
      ) : null}

      {grants(access, "ArchiveRecord") && status && status !== "Archived" ? (
        <Archive scope={scope} recordId={recordId!} />
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

                  {corrections
                    .filter((correction) => correction.targetRevisionNumber === revision.number)
                    .map((correction, index) => (
                      <p key={`${correction.requestedAt}-${index}`} className="mt-2 text-xs">
                        Sent back <When value={correction.requestedAt} /> by{" "}
                        <span className="font-mono">{correction.requestedBy}</span>:{" "}
                        <span>{correction.reason}</span>
                      </p>
                    ))}
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

type Scope = { workspaceId: string; projectId: string };

type Correction = ViewRecordHistoryResult["corrections"][number];

function Content({ record }: { record: GetRecordResult }) {
  const fields = Object.entries(record.frontMatter).sort(([left], [right]) =>
    left < right ? -1 : left > right ? 1 : 0,
  );

  return (
    <article className="space-y-3">
      <p className="text-xs text-[var(--color-muted)]">
        Revision {record.revisionNumber}
        {record.publishedRevisionNumber == null
          ? " · never published"
          : record.publishedRevisionNumber === record.revisionNumber
            ? " · published"
            : ` · not published, readers see revision ${record.publishedRevisionNumber}`}
        {record.provenance.isAiGenerated ? " · drafted by AI" : ""} · from{" "}
        {record.provenance.sourceLocator} · recorded by {record.provenance.author}
      </p>

      {/* Part of what an approval binds to, so it is shown rather than taken on trust. */}
      {fields.length > 0 ? (
        <dl aria-label="Front matter" className="grid grid-cols-[max-content_1fr] gap-x-3 gap-y-1 text-xs">
          {fields.map(([key, value]) => (
            <Fragment key={key}>
              <dt className="font-mono">{key}</dt>
              <dd>{value}</dd>
            </Fragment>
          ))}
        </dl>
      ) : null}

      <pre className="overflow-x-auto whitespace-pre-wrap rounded bg-neutral-50 p-3 text-sm">
        {record.body}
      </pre>

      {record.evidence.length > 0 ? (
        <ul aria-label="Evidence" className="space-y-1 text-xs">
          {record.evidence.map((reference) => (
            <li key={reference.evidenceObjectId}>
              Evidence: {reference.description} <span className="font-mono">{reference.evidenceObjectId}</span>
            </li>
          ))}
        </ul>
      ) : null}
    </article>
  );
}

/** Every step changes the status, the revision shown, and which list the record belongs in. */
function useRecordRefresh(scope: Scope, recordId: string): () => Promise<void> {
  const queries = useQueryClient();

  return async () => {
    await queries.invalidateQueries({ queryKey: ["record", scope.workspaceId, scope.projectId, recordId] });
    await queries.invalidateQueries({ queryKey: ["history", scope.workspaceId, scope.projectId, recordId] });
    await queries.invalidateQueries({ queryKey: ["records", scope.workspaceId, scope.projectId] });
  };
}

/**
 * Writing the next revision of a draft.
 *
 * It starts from the newest revision, asked for by number, and sends back everything it was given
 * that the person did not change. A revision stores exactly what the request carries, so an
 * editor that forgot the front matter or the evidence would drop them without anybody noticing,
 * and the front matter is part of what the next approval binds to.
 */
function Revise({
  scope,
  recordId,
  revisionNumber,
  sentBack,
}: {
  scope: Scope;
  recordId: string;
  revisionNumber: number;
  sentBack: Correction[];
}) {
  const [editing, setEditing] = useState(false);

  const record = useQuery({
    queryKey: ["record", scope.workspaceId, scope.projectId, recordId, revisionNumber],
    queryFn: () => invoke("get_record", { scope, recordId, revisionNumber }),
  });

  return (
    <Panel title="Revise this draft">
      <div className="space-y-3">
        {sentBack.map((correction, index) => (
          <Alert key={`${correction.requestedAt}-${index}`} tone="error">
            Sent back <When value={correction.requestedAt} />: {correction.reason}
          </Alert>
        ))}

        {!editing ? (
          <Button onClick={() => setEditing(true)}>Edit this draft</Button>
        ) : record.isPending ? (
          <Empty>Loading…</Empty>
        ) : record.isError ? (
          <Failure error={record.error} />
        ) : (
          <ReviseForm
            key={record.data.revisionNumber}
            scope={scope}
            recordId={recordId}
            record={record.data}
            onDone={() => setEditing(false)}
          />
        )}
      </div>
    </Panel>
  );
}

interface FrontMatterRow {
  key: string;
  value: string;
}

function ReviseForm({
  scope,
  recordId,
  record,
  onDone,
}: {
  scope: Scope;
  recordId: string;
  record: GetRecordResult;
  onDone: () => void;
}) {
  const refresh = useRecordRefresh(scope, recordId);
  const { user } = useSession();

  const [title, setTitle] = useState(record.title);
  const [body, setBody] = useState(record.body);
  const [rows, setRows] = useState<FrontMatterRow[]>(() =>
    Object.entries(record.frontMatter).map(([key, value]) => ({ key, value })),
  );

  const names = rows.map((row) => row.key.trim()).filter((name) => name !== "");
  const duplicated = names.find((name, index) => names.indexOf(name) !== index);

  const revise = useMutation({
    mutationFn: () =>
      invoke("revise_draft", {
        scope,
        recordId,
        title,
        body,
        frontMatter: Object.fromEntries(
          rows.filter((row) => row.key.trim() !== "").map((row) => [row.key.trim(), row.value]),
        ),
        // Where the content came from has not changed, so it is carried over. Whether an AI wrote
        // it is not the page's to say: the server sets that from the channel, and a revision of AI
        // content stays marked.
        provenance: {
          sourceKind: record.provenance.sourceKind,
          sourceLocator: record.provenance.sourceLocator,
          author: user?.displayName || user?.email || "A person",
          recordedAt: new Date().toISOString(),
          evidence: record.evidence,
        },
      }),
    onSuccess: async () => {
      onDone();
      await refresh();
    },
  });

  function update(index: number, change: Partial<FrontMatterRow>): void {
    setRows((current) => current.map((row, at) => (at === index ? { ...row, ...change } : row)));
  }

  return (
    <form
      className="space-y-3"
      onSubmit={(event) => {
        event.preventDefault();
        revise.mutate();
      }}
    >
      <p className="text-sm text-[var(--color-muted)]">
        Editing revision {record.revisionNumber}. Saving adds revision {record.revisionNumber + 1}; this one
        stays in the history as it is.
      </p>

      <Field label="Title">
        <Input required value={title} onChange={(event) => setTitle(event.target.value)} />
      </Field>

      <Field label="Body">
        <TextArea rows={8} value={body} onChange={(event) => setBody(event.target.value)} />
      </Field>

      <fieldset className="space-y-2">
        <legend className="text-sm font-medium">Front matter</legend>
        {rows.map((row, index) => (
          <div key={index} className="flex gap-2">
            <Input
              aria-label={`Field ${index + 1} name`}
              value={row.key}
              onChange={(event) => update(index, { key: event.target.value })}
            />
            <Input
              aria-label={`Field ${index + 1} value`}
              value={row.value}
              onChange={(event) => update(index, { value: event.target.value })}
            />
            <Button
              aria-label={`Remove field ${index + 1}`}
              onClick={() => setRows((current) => current.filter((_, at) => at !== index))}
            >
              Remove
            </Button>
          </div>
        ))}
        <Button onClick={() => setRows((current) => [...current, { key: "", value: "" }])}>Add a field</Button>
        {duplicated ? <Alert tone="error">The field “{duplicated}” is named twice.</Alert> : null}
      </fieldset>

      {record.evidence.length > 0 ? (
        <div className="text-sm">
          <p className="font-medium">Evidence, kept with the new revision</p>
          <ul className="text-xs">
            {record.evidence.map((reference) => (
              <li key={reference.evidenceObjectId}>{reference.description}</li>
            ))}
          </ul>
        </div>
      ) : null}

      <div className="flex gap-2">
        <Button type="submit" variant="primary" disabled={revise.isPending || Boolean(duplicated)}>
          Save as a new revision
        </Button>
        <Button onClick={onDone}>Cancel</Button>
      </div>

      {revise.isError ? <Failure error={revise.error} /> : null}
    </form>
  );
}

function Submit({ scope, recordId, revision }: { scope: Scope; recordId: string; revision: Revision }) {
  const refresh = useRecordRefresh(scope, recordId);

  // Submission names no hash, and does not need to: it binds nothing. The approval that follows
  // binds the exact content the reviewer reads, so a revision added after this click is caught
  // there rather than here.
  const submit = useMutation({
    mutationFn: () => invoke("submit_for_approval", { scope, recordId }),
    onSuccess: refresh,
  });

  return (
    <Panel title="Submit for approval">
      <div className="space-y-3">
        <p className="text-sm text-[var(--color-muted)]">
          Revision {revision.number} is a draft, and nobody is asked to review a draft. Submitting it
          puts it in the review queue, where a reviewer approves this exact content or sends it back.
          Submitting publishes nothing.
        </p>
        <Button variant="primary" disabled={submit.isPending} onClick={() => submit.mutate()}>
          Submit for approval
        </Button>
        {submit.isError ? <Failure error={submit.error} /> : null}
      </div>
    </Panel>
  );
}

function Approval({ scope, recordId, revision }: { scope: Scope; recordId: string; revision: Revision }) {
  const refresh = useRecordRefresh(scope, recordId);
  const [reason, setReason] = useState("");

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
          <Field label="Or send it back" hint="The reason is kept on the record and shown to whoever revises it.">
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

function Publish({ scope, recordId }: { scope: Scope; recordId: string }) {
  const refresh = useRecordRefresh(scope, recordId);

  const publish = useMutation({
    mutationFn: () => invoke("publish_record", { scope, recordId }),
    onSuccess: refresh,
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

function Archive({ scope, recordId }: { scope: Scope; recordId: string }) {
  const refresh = useRecordRefresh(scope, recordId);
  const [armed, setArmed] = useState(false);

  const archive = useMutation({
    mutationFn: () => invoke("archive_record", { scope, recordId }),
    onSuccess: async () => {
      setArmed(false);
      await refresh();
    },
  });

  // Confirmed rather than one click, because nothing reverses it: an archived record refuses
  // every further change, and no operation takes it out of that state.
  return (
    <Panel title="Archive">
      <div className="space-y-3">
        <p className="text-sm text-[var(--color-muted)]">
          Archiving takes this record out of use. An archived record cannot be revised, approved, or
          published again, and there is no way to bring it back. Its history stays readable.
        </p>
        {armed ? (
          <div className="flex gap-2">
            <Button variant="danger" disabled={archive.isPending} onClick={() => archive.mutate()}>
              Archive for good
            </Button>
            <Button onClick={() => setArmed(false)}>Cancel</Button>
          </div>
        ) : (
          <Button variant="danger" onClick={() => setArmed(true)}>
            Archive
          </Button>
        )}
        {archive.isError ? <Failure error={archive.error} /> : null}
      </div>
    </Panel>
  );
}
