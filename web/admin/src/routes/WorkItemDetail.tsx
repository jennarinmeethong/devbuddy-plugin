import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { grants, useSession, useWorkspace } from "../api/session";
import type { GenerateHandoverResult } from "../api/operations";
import { refetchAfterWrite } from "../api/queries";
import { Alert, Badge, Button, Empty, Field, Input, Panel, Select, Table, TextArea, When } from "../components/ui";
import { Failure } from "../components/Failure";
import { ProjectNav } from "../components/ProjectNav";
import {
  KIND_LABELS,
  KINDS,
  SOURCE_KIND_LABELS,
  SOURCE_KINDS,
  STATUS_LABELS,
  type RecordKind,
  type SourceKind,
} from "../components/labels";

type Scope = { workspaceId: string; projectId: string };

/**
 * One piece of work: what it is for, the knowledge written against it, and what a handover of it
 * would say today.
 *
 * This is where a person writes a new draft. A draft hangs off a work item, so the form lives with
 * the work item rather than on the record list, where it would first have to ask which one.
 */
export function WorkItemDetail() {
  const { workspaceId, projectId, workItemId } = useParams();
  const access = useWorkspace(workspaceId);
  const scope: Scope = { workspaceId: workspaceId!, projectId: projectId! };

  const item = useQuery({
    queryKey: ["work-item", workspaceId, projectId, workItemId],
    queryFn: () => invoke("get_work_item", { scope, workItemId: workItemId! }),
    enabled: Boolean(workspaceId && projectId && workItemId),
  });

  const records = useQuery({
    queryKey: ["records", workspaceId, projectId, "All"],
    queryFn: () => invoke("list_records", { scope, statuses: null }),
    enabled: Boolean(workspaceId && projectId),
  });

  const mine = records.data?.records.filter((record) => record.workItemId === workItemId) ?? [];

  return (
    <>
      <ProjectNav title={item.data ? `${item.data.key} · ${item.data.title}` : "Work item"} />

      <Panel title="The work">
        {item.isPending ? (
          <Empty>Loading…</Empty>
        ) : item.isError ? (
          <Failure error={item.error} />
        ) : (
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <Fact label="Goal">{item.data.goal}</Fact>
            <Fact label="Type">{item.data.type}</Fact>
            <Fact label="In scope">{item.data.inScope ?? "—"}</Fact>
            <Fact label="Deliberately excluded">{item.data.exclusions ?? "—"}</Fact>
            <Fact label="Stakeholders">
              {item.data.stakeholders.length > 0 ? item.data.stakeholders.join(", ") : "—"}
            </Fact>
            <Fact label="Records">{String(item.data.recordCount)}</Fact>
          </dl>
        )}
      </Panel>

      <Panel title="Knowledge written for this work">
        {records.isPending ? (
          <Empty>Loading…</Empty>
        ) : records.isError ? (
          <Failure error={records.error} />
        ) : mine.length === 0 ? (
          <Empty>Nothing written yet.</Empty>
        ) : (
          <Table head={["Title", "Kind", "Status", "Updated"]}>
            {mine.map((record) => (
              <tr key={record.recordId} className="border-b border-[var(--color-line)] last:border-0">
                <td className="px-2 py-2">
                  <Link className="underline" to={`/w/${workspaceId}/p/${projectId}/records/${record.recordId}`}>
                    {record.title}
                  </Link>
                </td>
                <td className="px-2 py-2 text-xs text-[var(--color-muted)]">{KIND_LABELS[record.kind]}</td>
                <td className="px-2 py-2">
                  {record.status === "Published" ? (
                    <Badge tone="live">Published</Badge>
                  ) : (
                    <Badge>{STATUS_LABELS[record.status]}</Badge>
                  )}
                </td>
                <td className="px-2 py-2 text-xs">
                  <When value={record.updatedAt} />
                </td>
              </tr>
            ))}
          </Table>
        )}
      </Panel>

      {grants(access, "CreateDraft") && workItemId ? <NewDraft scope={scope} workItemId={workItemId} /> : null}

      {workItemId ? <Handover scope={scope} workItemId={workItemId} /> : null}
    </>
  );
}

function Fact({ label, children }: { label: string; children: string }) {
  return (
    <div>
      <dt className="text-xs uppercase tracking-wide text-[var(--color-muted)]">{label}</dt>
      <dd className="whitespace-pre-wrap">{children}</dd>
    </div>
  );
}

interface FrontMatterRow {
  key: string;
  value: string;
}

/**
 * A new draft, written by a person.
 *
 * Provenance is asked for rather than assumed, because it is what lets a later reader check a
 * record against where it came from. Whether an AI wrote it is not asked at all: the server sets
 * that from the channel, and a person may only say that what they are pasting came from one.
 */
function NewDraft({ scope, workItemId }: { scope: Scope; workItemId: string }) {
  const queries = useQueryClient();
  const navigate = useNavigate();
  const { user } = useSession();

  const [kind, setKind] = useState<RecordKind>("Decision");
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [sourceKind, setSourceKind] = useState<SourceKind>("HumanAuthored");
  const [sourceLocator, setSourceLocator] = useState("");
  const [rows, setRows] = useState<FrontMatterRow[]>([]);
  const [evidence, setEvidence] = useState<Record<string, string>>({});

  const available = useQuery({
    queryKey: ["evidence", scope.workspaceId, scope.projectId],
    queryFn: () => invoke("list_evidence", { scope }),
  });

  const names = rows.map((row) => row.key.trim()).filter((name) => name !== "");
  const duplicated = names.find((name, index) => names.indexOf(name) !== index);
  const undescribed = Object.values(evidence).some((description) => description.trim() === "");

  const create = useMutation({
    mutationFn: () =>
      invoke("create_draft", {
        scope,
        workItemId,
        kind,
        title,
        body,
        frontMatter: Object.fromEntries(
          rows.filter((row) => row.key.trim() !== "").map((row) => [row.key.trim(), row.value]),
        ),
        provenance: {
          sourceKind,
          sourceLocator,
          author: user?.displayName || user?.email || "A person",
          recordedAt: new Date().toISOString(),
          evidence: Object.entries(evidence).map(([evidenceObjectId, description]) => ({
            evidenceObjectId,
            description: description.trim(),
          })),
        },
      }),
    onSuccess: async (result) => {
      await refetchAfterWrite(queries, { queryKey: ["records", scope.workspaceId, scope.projectId] });
      await refetchAfterWrite(queries, { queryKey: ["work-item", scope.workspaceId, scope.projectId, workItemId] });
      navigate(`/w/${scope.workspaceId}/p/${scope.projectId}/records/${result.recordId}`);
    },
  });

  function update(index: number, change: Partial<FrontMatterRow>): void {
    setRows((current) => current.map((row, at) => (at === index ? { ...row, ...change } : row)));
  }

  function toggle(evidenceId: string, on: boolean): void {
    setEvidence((current) => {
      const next = { ...current };

      if (on) {
        next[evidenceId] = "";
      } else {
        delete next[evidenceId];
      }

      return next;
    });
  }

  return (
    <Panel title="Write a new draft">
      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault();
          create.mutate();
        }}
      >
        <p className="text-sm text-[var(--color-muted)]">
          A draft is not knowledge yet. Nobody reading published knowledge sees it until it has been
          submitted, approved and published.
        </p>

        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Kind">
            <Select value={kind} onChange={(event) => setKind(event.target.value as RecordKind)}>
              {KINDS.map((option) => (
                <option key={option} value={option}>
                  {KIND_LABELS[option]}
                </option>
              ))}
            </Select>
          </Field>

          <Field label="Where it came from">
            <Select value={sourceKind} onChange={(event) => setSourceKind(event.target.value as SourceKind)}>
              {SOURCE_KINDS.map((option) => (
                <option key={option} value={option}>
                  {SOURCE_KIND_LABELS[option]}
                </option>
              ))}
            </Select>
          </Field>
        </div>

        <Field label="Source" hint="Where a later reader can check this: a meeting, a document, a commit, a ticket.">
          <Input required value={sourceLocator} onChange={(event) => setSourceLocator(event.target.value)} />
        </Field>

        <Field label="Title">
          <Input required value={title} onChange={(event) => setTitle(event.target.value)} />
        </Field>

        <Field label="Body" hint="Markdown. The context, the conclusion, and why.">
          <TextArea required rows={8} value={body} onChange={(event) => setBody(event.target.value)} />
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

        <fieldset className="space-y-2">
          <legend className="text-sm font-medium">Evidence</legend>
          {available.isPending ? (
            <p className="text-xs text-[var(--color-muted)]">Loading…</p>
          ) : available.isError ? (
            <Failure error={available.error} />
          ) : available.data.evidence.length === 0 ? (
            <p className="text-xs text-[var(--color-muted)]">This project holds no evidence yet.</p>
          ) : (
            available.data.evidence.map((item) => (
              <div key={item.evidenceId} className="flex flex-wrap items-center gap-2 text-sm">
                <label className="flex items-center gap-2">
                  <input
                    type="checkbox"
                    checked={item.evidenceId in evidence}
                    onChange={(event) => toggle(item.evidenceId, event.target.checked)}
                  />
                  <span className="font-mono text-xs">{item.evidenceId}</span>
                  <span className="text-xs text-[var(--color-muted)]">{item.mediaType}</span>
                </label>
                {item.evidenceId in evidence ? (
                  <Input
                    aria-label={`What evidence ${item.evidenceId} shows`}
                    placeholder="What this shows"
                    className="max-w-sm"
                    value={evidence[item.evidenceId]}
                    onChange={(event) =>
                      setEvidence((current) => ({ ...current, [item.evidenceId]: event.target.value }))
                    }
                  />
                ) : null}
              </div>
            ))
          )}
        </fieldset>

        <Button
          type="submit"
          variant="primary"
          disabled={create.isPending || Boolean(duplicated) || undescribed}
        >
          Save draft
        </Button>

        {create.isError ? <Failure error={create.error} /> : null}
      </form>
    </Panel>
  );
}

/**
 * What a handover of this work would say, assembled from published knowledge, and the two checks a
 * person taking it over asks first. Each runs on request: they read every record for the work.
 */
function Handover({ scope, workItemId }: { scope: Scope; workItemId: string }) {
  const handover = useMutation({
    mutationFn: () => invoke("generate_handover", { scope, workItemId }),
  });

  const questions = useMutation({
    mutationFn: () => invoke("find_open_questions", { scope, workItemId }),
  });

  const gaps = useMutation({
    mutationFn: () => invoke("find_missing_evidence", { scope, workItemId }),
  });

  return (
    <Panel title="Handing this work over">
      <div className="space-y-4">
        <div className="flex flex-wrap gap-2">
          <Button onClick={() => handover.mutate()} disabled={handover.isPending}>
            Generate a handover
          </Button>
          <Button onClick={() => questions.mutate()} disabled={questions.isPending}>
            Find open questions
          </Button>
          <Button onClick={() => gaps.mutate()} disabled={gaps.isPending}>
            Find missing evidence
          </Button>
        </div>

        {handover.isError ? <Failure error={handover.error} /> : null}
        {handover.data ? <HandoverView handover={handover.data} /> : null}

        {questions.isError ? <Failure error={questions.error} /> : null}
        {questions.data ? (
          <ListResult title="Open questions" items={questions.data.questions} none="No open questions found." />
        ) : null}

        {gaps.isError ? <Failure error={gaps.error} /> : null}
        {gaps.data ? (
          <ListResult title="Missing evidence" items={gaps.data.gaps} none="No gaps in the evidence found." />
        ) : null}
      </div>
    </Panel>
  );
}

function HandoverView({ handover }: { handover: GenerateHandoverResult }) {
  return (
    <article className="space-y-3 rounded-md border border-[var(--color-line)] p-3">
      <header className="flex items-baseline justify-between gap-2">
        <h3 className="font-semibold">{handover.title}</h3>
        <span className="text-xs text-[var(--color-muted)]">
          Generated <When value={handover.generatedAt} />
        </span>
      </header>

      {handover.sections.length === 0 ? (
        <Empty>No published knowledge to hand over yet.</Empty>
      ) : (
        handover.sections.map((section) => (
          <section key={section.kind}>
            <h4 className="text-sm font-medium">
              {KIND_LABELS[section.kind]}{" "}
              <span className="text-xs text-[var(--color-muted)]">({section.recordCount})</span>
            </h4>
            <p className="whitespace-pre-wrap text-sm">{section.content}</p>
          </section>
        ))
      )}

      <ListResult title="Open questions" items={handover.openQuestions} none="None." />
      <ListResult title="Missing evidence" items={handover.missingEvidence} none="None." />
    </article>
  );
}

function ListResult({ title, items, none }: { title: string; items: string[]; none: string }) {
  return (
    <div>
      <h4 className="text-sm font-medium">{title}</h4>
      {items.length === 0 ? (
        <p className="text-sm text-[var(--color-muted)]">{none}</p>
      ) : (
        <ul className="list-disc pl-5 text-sm">
          {items.map((entry, index) => (
            <li key={index}>{entry}</li>
          ))}
        </ul>
      )}
    </div>
  );
}
