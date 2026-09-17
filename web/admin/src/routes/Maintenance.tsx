import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { grants, useWorkspace } from "../api/session";
import type { ValidateProvenanceResult } from "../api/operations";
import { Alert, Badge, Button, Field, Input, Panel, Table, TextArea, When } from "../components/ui";
import { Failure } from "../components/Failure";
import { ProjectNav } from "../components/ProjectNav";

type Scope = { workspaceId: string; projectId: string };

type Finding = ValidateProvenanceResult["findings"][number];

/**
 * Keeping a project's knowledge in order: the quality sweeps, the search index, checking text for
 * what must not be stored, and taking a copy out.
 *
 * Each panel appears only to a person whose role carries its permission. The sweeps report and
 * change nothing; a person decides what to do about a finding.
 */
export function Maintenance() {
  const { workspaceId, projectId } = useParams();
  const access = useWorkspace(workspaceId);
  const scope: Scope = { workspaceId: workspaceId!, projectId: projectId! };

  return (
    <>
      <ProjectNav title="Maintenance" />
      {grants(access, "ManageIndex") ? <Sweeps scope={scope} /> : null}
      {grants(access, "ManageIndex") ? <Reindex scope={scope} /> : null}
      {grants(access, "ScanContent") ? <CheckText scope={scope} /> : null}
      {grants(access, "AdministerSystem") ? <Export scope={scope} /> : null}
    </>
  );
}

function Sweeps({ scope }: { scope: Scope }) {
  const [days, setDays] = useState(180);

  const provenance = useMutation({ mutationFn: () => invoke("validate_provenance", { scope }) });
  const duplicates = useMutation({ mutationFn: () => invoke("detect_duplicates", { scope }) });

  // A TimeSpan on the wire is "d.hh:mm:ss".
  const staleness = useMutation({
    mutationFn: () => invoke("detect_staleness", { scope, staleAfter: `${days}.00:00:00` }),
  });

  return (
    <Panel title="Quality sweeps">
      <div className="space-y-4">
        <p className="text-sm text-[var(--color-muted)]">
          Each sweep reads the project&apos;s records and reports what it finds. Nothing is changed.
        </p>

        <div className="flex flex-wrap items-end gap-2">
          <Button onClick={() => provenance.mutate()} disabled={provenance.isPending}>
            Check provenance
          </Button>
          <Button onClick={() => duplicates.mutate()} disabled={duplicates.isPending}>
            Find duplicates
          </Button>
          <div className="w-44">
            <Field label="Untouched for (days)">
              <Input
                type="number"
                min={1}
                value={days}
                onChange={(event) => setDays(Math.max(1, Number(event.target.value) || 1))}
              />
            </Field>
          </div>
          <Button onClick={() => staleness.mutate()} disabled={staleness.isPending}>
            Find stale records
          </Button>
        </div>

        <SweepResult title="Provenance" state={provenance} scope={scope} />
        <SweepResult title="Duplicates" state={duplicates} scope={scope} />
        <SweepResult title="Stale records" state={staleness} scope={scope} />
      </div>
    </Panel>
  );
}

function SweepResult({
  title,
  state,
  scope,
}: {
  title: string;
  state: { isError: boolean; error: Error | null; data?: { findings: Finding[] } };
  scope: Scope;
}) {
  if (state.isError && state.error) {
    return <Failure error={state.error} />;
  }

  if (!state.data) {
    return null;
  }

  return (
    <div>
      <h3 className="text-sm font-medium">
        {title} <Badge>{state.data.findings.length}</Badge>
      </h3>
      {state.data.findings.length === 0 ? (
        <p className="text-sm text-[var(--color-muted)]">Nothing found.</p>
      ) : (
        <Table head={["Record", "Rule", "Detail"]}>
          {state.data.findings.map((finding, index) => (
            <tr key={index} className="border-b border-[var(--color-line)] last:border-0">
              <td className="px-2 py-2">
                <Link
                  className="font-mono text-xs underline"
                  to={`/w/${scope.workspaceId}/p/${scope.projectId}/records/${finding.recordId}`}
                >
                  {finding.recordId}
                </Link>
              </td>
              <td className="px-2 py-2 text-xs">{finding.rule}</td>
              <td className="px-2 py-2 text-xs">{finding.detail}</td>
            </tr>
          ))}
        </Table>
      )}
    </div>
  );
}

function Reindex({ scope }: { scope: Scope }) {
  const reindex = useMutation({ mutationFn: () => invoke("reindex", { scope }) });

  return (
    <Panel title="Search index">
      <div className="space-y-3">
        <p className="text-sm text-[var(--color-muted)]">
          Rebuilds this project&apos;s full-text index from its records. The semantic index is kept by the
          embedding worker, not by this.
        </p>
        <Button onClick={() => reindex.mutate()} disabled={reindex.isPending}>
          Rebuild the index
        </Button>
        {reindex.isError ? <Failure error={reindex.error} /> : null}
        {reindex.data ? <Alert tone="success">{reindex.data.documentsIndexed} record(s) indexed.</Alert> : null}
      </div>
    </Panel>
  );
}

/**
 * Checking text before it goes anywhere. The findings name a rule and a line, never the matched
 * text, which is the same promise the server makes everywhere else.
 */
function CheckText({ scope }: { scope: Scope }) {
  const [content, setContent] = useState("");

  const detect = useMutation({ mutationFn: () => invoke("detect_secrets", { scope, content }) });
  const redact = useMutation({ mutationFn: () => invoke("redact_sensitive_data", { scope, content }) });

  return (
    <Panel title="Check text">
      <div className="space-y-3">
        <Field label="Text" hint="Nothing typed here is stored.">
          <TextArea rows={6} value={content} onChange={(event) => setContent(event.target.value)} />
        </Field>

        <div className="flex gap-2">
          <Button onClick={() => detect.mutate()} disabled={detect.isPending || content === ""}>
            Look for secrets
          </Button>
          <Button onClick={() => redact.mutate()} disabled={redact.isPending || content === ""}>
            Redact it
          </Button>
        </div>

        {detect.isError ? <Failure error={detect.error} /> : null}
        {detect.data ? (
          detect.data.hasFindings ? (
            <Table head={["Rule", "Line", "Length"]}>
              {detect.data.findings.map((finding, index) => (
                <tr key={index} className="border-b border-[var(--color-line)] last:border-0">
                  <td className="px-2 py-2">{finding.ruleName}</td>
                  <td className="px-2 py-2">{finding.lineNumber}</td>
                  <td className="px-2 py-2">{finding.length}</td>
                </tr>
              ))}
            </Table>
          ) : (
            <Alert tone="success">No secrets found.</Alert>
          )
        ) : null}

        {redact.isError ? <Failure error={redact.error} /> : null}
        {redact.data ? (
          <div className="space-y-1">
            <p className="text-sm">{redact.data.findingCount} finding(s) redacted.</p>
            <pre className="whitespace-pre-wrap rounded-md border border-[var(--color-line)] bg-neutral-50 p-2 text-xs">
              {redact.data.redactedContent}
            </pre>
          </div>
        ) : null}
      </div>
    </Panel>
  );
}

function Export({ scope }: { scope: Scope }) {
  const exported = useMutation({ mutationFn: () => invoke("export_project", { scope }) });

  return (
    <Panel title="Export">
      <div className="space-y-3">
        <p className="text-sm text-[var(--color-muted)]">
          Writes a copy of this project — records, work items and evidence — to the server&apos;s export
          volume, where the retention sweep removes it when it expires.
        </p>
        <Button onClick={() => exported.mutate()} disabled={exported.isPending}>
          Export this project
        </Button>
        {exported.isError ? <Failure error={exported.error} /> : null}
        {exported.data ? (
          <Alert tone="success">
            Export <span className="font-mono">{exported.data.reference}</span>: {exported.data.recordCount} record(s),{" "}
            {exported.data.evidenceCount} evidence item(s). Kept until <When value={exported.data.expiresAt} />.
          </Alert>
        ) : null}
      </div>
    </Panel>
  );
}
