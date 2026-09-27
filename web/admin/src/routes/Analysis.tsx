import { useState } from "react";
import { useParams } from "react-router-dom";
import { useMutation, useQuery } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { grants, useWorkspace } from "../api/session";
import type { AnalyzeProjectResult, OperationName } from "../api/operations";
import { Alert, Button, Empty, Field, Input, Panel, Select, Table } from "../components/ui";
import { Failure } from "../components/Failure";
import { ProjectNav } from "../components/ProjectNav";
import { m, t, tr } from "../i18n";

type Scope = { workspaceId: string; projectId: string };

type Observation = AnalyzeProjectResult["report"]["observations"][number];

/** The seven read-only analyses. They take the same arguments and answer in the same shape. */
const ANALYSES = [
  { operation: "analyze_project", label: m("The project as a whole") },
  { operation: "analyze_code", label: m("Code") },
  { operation: "analyze_documents", label: m("Documents") },
  { operation: "analyze_architecture", label: m("Architecture") },
  { operation: "analyze_git_history", label: m("Git history") },
  { operation: "analyze_work_items", label: m("Work items") },
  { operation: "analyze_test_evidence", label: m("Test evidence") },
] as const satisfies readonly { operation: OperationName; label: string }[];

type AnalysisOperation = (typeof ANALYSES)[number]["operation"];

/**
 * Looking at the project's source, read-only.
 *
 * Nothing here runs anything in the repository under study: every analysis reads files, and that
 * is the server's guarantee (SB-04), not this page's. A repository is chosen from what the
 * installation already makes reachable — a mounted working copy or a configured GitHub
 * repository — because an identifier nobody can see is not something a person can type.
 */
export function Analysis() {
  const { workspaceId, projectId } = useParams();
  const access = useWorkspace(workspaceId);
  const scope: Scope = { workspaceId: workspaceId!, projectId: projectId! };

  const repositories = useQuery({
    queryKey: ["source-repositories", workspaceId, projectId],
    queryFn: () => invoke("list_source_repositories", { scope }),
    enabled: Boolean(workspaceId && projectId),
  });

  const choices = repositories.data?.repositories ?? [];

  return (
    <>
      <ProjectNav title={t("Analysis")} />

      <Panel title={t("Repositories this project can read")}>
        {repositories.isPending ? (
          <Empty>{t("Loading…")}</Empty>
        ) : repositories.isError ? (
          <Failure error={repositories.error} />
        ) : choices.length === 0 ? (
          <Alert>
            {t("No repository is reachable for this project. An operator makes one reachable by mounting its working copy under the project's directory, named by a repository identifier, or by configuring it for the GitHub API. An analysis of the whole project can still run.")}
          </Alert>
        ) : (
          <Table head={[t("Repository"), t("Address")]}>
            {choices.map((repository) => (
              <tr key={repository.repositoryId} className="border-b border-[var(--color-line)] last:border-0">
                <td className="px-2 py-2 font-mono text-xs">{repository.repositoryId}</td>
                <td className="px-2 py-2 text-xs">{repository.locator ?? t("Mounted working copy")}</td>
              </tr>
            ))}
          </Table>
        )}
      </Panel>

      <RunAnalysis scope={scope} repositories={choices} />

      {choices.length > 0 ? (
        <>
          <ChangeImpact scope={scope} repositories={choices} />
          <CompareReferences scope={scope} repositories={choices} />
          {grants(access, "ManageSources") ? <Synchronise scope={scope} repositories={choices} /> : null}
        </>
      ) : null}
    </>
  );
}

type Repository = { repositoryId: string; locator: string | null };

function RepositoryChoice({
  repositories,
  value,
  onChange,
  allowWholeProject,
}: {
  repositories: Repository[];
  value: string;
  onChange: (value: string) => void;
  allowWholeProject: boolean;
}) {
  return (
    <Field label={t("Repository")}>
      <Select value={value} onChange={(event) => onChange(event.target.value)}>
        {allowWholeProject ? <option value="">{t("The whole project directory")}</option> : null}
        {repositories.map((repository) => (
          <option key={repository.repositoryId} value={repository.repositoryId}>
            {repository.locator ?? repository.repositoryId}
          </option>
        ))}
      </Select>
    </Field>
  );
}

function RunAnalysis({ scope, repositories }: { scope: Scope; repositories: Repository[] }) {
  const [operation, setOperation] = useState<AnalysisOperation>("analyze_project");
  const [repositoryId, setRepositoryId] = useState("");
  const [target, setTarget] = useState("");

  const run = useMutation({
    mutationFn: () =>
      invoke(operation, {
        scope,
        repositoryId: repositoryId || null,
        target: target.trim() || null,
      }),
  });

  return (
    <Panel title={t("Run an analysis")}>
      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault();
          run.mutate();
        }}
      >
        <div className="grid gap-3 sm:grid-cols-3">
          <Field label={t("What to look at")}>
            <Select value={operation} onChange={(event) => setOperation(event.target.value as AnalysisOperation)}>
              {ANALYSES.map((analysis) => (
                <option key={analysis.operation} value={analysis.operation}>
                  {t(analysis.label)}
                </option>
              ))}
            </Select>
          </Field>
          <RepositoryChoice repositories={repositories} value={repositoryId} onChange={setRepositoryId} allowWholeProject />
          <Field label={t("Path inside it")} hint={t("Optional. Relative, and it cannot leave the repository.")}>
            <Input value={target} onChange={(event) => setTarget(event.target.value)} />
          </Field>
        </div>

        <Button type="submit" variant="primary" disabled={run.isPending}>
          {t("Analyse")}
        </Button>
      </form>

      {run.isError ? (
        <div className="mt-3">
          <Failure error={run.error} />
        </div>
      ) : null}

      {run.data ? (
        <div className="mt-4 space-y-2">
          <p className="text-sm">{run.data.report.summary}</p>
          <Observations observations={run.data.report.observations} />
        </div>
      ) : null}
    </Panel>
  );
}

function Observations({ observations }: { observations: Observation[] }) {
  if (observations.length === 0) {
    return <Empty>{t("Nothing to report.")}</Empty>;
  }

  return (
    <Table head={[t("Subject"), t("Detail"), t("Where")]}>
      {observations.map((observation, index) => (
        <tr key={index} className="border-b border-[var(--color-line)] last:border-0">
          <td className="px-2 py-2">{observation.subject}</td>
          <td className="px-2 py-2 whitespace-pre-wrap text-xs">{observation.detail}</td>
          <td className="px-2 py-2 font-mono text-xs">{observation.sourceLocator}</td>
        </tr>
      ))}
    </Table>
  );
}

function ChangeImpact({ scope, repositories }: { scope: Scope; repositories: Repository[] }) {
  const [repositoryId, setRepositoryId] = useState(repositories[0]?.repositoryId ?? "");
  const [commitOrRange, setCommitOrRange] = useState("");

  const run = useMutation({
    mutationFn: () => invoke("analyze_change_impact", { scope, repositoryId, commitOrRange }),
  });

  return (
    <Panel title={t("What a change affects")}>
      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault();
          run.mutate();
        }}
      >
        <div className="grid gap-3 sm:grid-cols-2">
          <RepositoryChoice
            repositories={repositories}
            value={repositoryId}
            onChange={setRepositoryId}
            allowWholeProject={false}
          />
          <Field label={t("Commit or range")} hint={t("A commit, or two joined by .. such as main..feature.")}>
            <Input required value={commitOrRange} onChange={(event) => setCommitOrRange(event.target.value)} />
          </Field>
        </div>
        <Button type="submit" variant="primary" disabled={run.isPending}>
          {t("Work out the impact")}
        </Button>
      </form>

      {run.isError ? (
        <div className="mt-3">
          <Failure error={run.error} />
        </div>
      ) : null}

      {run.data ? (
        <div className="mt-4 space-y-3">
          <div>
            <h3 className="text-sm font-medium">{t("Changed paths ({length})", { length: run.data.changedPaths.length })}</h3>
            <ul className="font-mono text-xs">
              {run.data.changedPaths.map((path) => (
                <li key={path}>{path}</li>
              ))}
            </ul>
          </div>
          <Observations observations={run.data.impact} />
        </div>
      ) : null}
    </Panel>
  );
}

function CompareReferences({ scope, repositories }: { scope: Scope; repositories: Repository[] }) {
  const [repositoryId, setRepositoryId] = useState(repositories[0]?.repositoryId ?? "");
  const [earlierReference, setEarlier] = useState("");
  const [laterReference, setLater] = useState("");

  const run = useMutation({
    mutationFn: () => invoke("compare_snapshots", { scope, repositoryId, earlierReference, laterReference }),
  });

  return (
    <Panel title={t("Compare two references")}>
      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault();
          run.mutate();
        }}
      >
        <div className="grid gap-3 sm:grid-cols-3">
          <RepositoryChoice
            repositories={repositories}
            value={repositoryId}
            onChange={setRepositoryId}
            allowWholeProject={false}
          />
          <Field label={t("Earlier")} hint={t("A branch, a tag or a commit.")}>
            <Input required value={earlierReference} onChange={(event) => setEarlier(event.target.value)} />
          </Field>
          <Field label={t("Later")}>
            <Input required value={laterReference} onChange={(event) => setLater(event.target.value)} />
          </Field>
        </div>
        <Button type="submit" variant="primary" disabled={run.isPending}>
          {t("Compare")}
        </Button>
      </form>

      {run.isError ? (
        <div className="mt-3">
          <Failure error={run.error} />
        </div>
      ) : null}

      {run.data ? (
        <div className="mt-4 space-y-3 text-sm">
          <p>
            {tr("{earlier} is {earlierCommit}; {later} is {laterCommit}.", {
              earlier: <span className="font-mono text-xs">{run.data.earlier.reference}</span>,
              earlierCommit: <span className="font-mono text-xs">{run.data.earlier.commitId}</span>,
              later: <span className="font-mono text-xs">{run.data.later.reference}</span>,
              laterCommit: <span className="font-mono text-xs">{run.data.later.commitId}</span>,
            })}
          </p>

          {run.data.differences.length === 0 ? (
            <Empty>{t("No differences.")}</Empty>
          ) : (
            <Table head={[t("What"), t("Before"), t("After")]}>
              {run.data.differences.map((difference, index) => (
                <tr key={index} className="border-b border-[var(--color-line)] last:border-0">
                  <td className="px-2 py-2">{difference.subject}</td>
                  <td className="px-2 py-2 font-mono text-xs">{difference.before}</td>
                  <td className="px-2 py-2 font-mono text-xs">{difference.after}</td>
                </tr>
              ))}
            </Table>
          )}

          {run.data.changedPaths ? (
            <div>
              <h3 className="font-medium">{t("Changed paths ({length})", { length: run.data.changedPaths.length })}</h3>
              <ul className="font-mono text-xs">
                {run.data.changedPaths.map((path) => (
                  <li key={path}>{path}</li>
                ))}
              </ul>
            </div>
          ) : run.data.changedPathsUnavailable ? (
            <Alert>{run.data.changedPathsUnavailable}</Alert>
          ) : null}
        </div>
      ) : null}
    </Panel>
  );
}

function Synchronise({ scope, repositories }: { scope: Scope; repositories: Repository[] }) {
  const [repositoryId, setRepositoryId] = useState(repositories[0]?.repositoryId ?? "");

  const sync = useMutation({
    mutationFn: () => invoke("sync_sources", { scope, repositoryId }),
  });

  return (
    <Panel title={t("Synchronise a repository")}>
      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault();
          sync.mutate();
        }}
      >
        <p className="text-sm text-[var(--color-muted)]">
          {t("Reads a snapshot of where the repository stands now. One way only: nothing is written back.")}
        </p>
        <div className="max-w-md">
          <RepositoryChoice
            repositories={repositories}
            value={repositoryId}
            onChange={setRepositoryId}
            allowWholeProject={false}
          />
        </div>
        <Button type="submit" variant="primary" disabled={sync.isPending}>
          {t("Synchronise")}
        </Button>
      </form>

      {sync.isError ? (
        <div className="mt-3">
          <Failure error={sync.error} />
        </div>
      ) : null}

      {sync.data ? (
        <dl className="mt-4 grid gap-2 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-xs text-[var(--color-muted)]">{t("Commit")}</dt>
            <dd className="font-mono text-xs">{sync.data.commitId}</dd>
          </div>
          <div>
            <dt className="text-xs text-[var(--color-muted)]">{t("References recorded")}</dt>
            <dd>{sync.data.linkCount}</dd>
          </div>
          <div>
            <dt className="text-xs text-[var(--color-muted)]">{t("Open pull requests")}</dt>
            {/* Null means this source system cannot say, which is not the same as none. */}
            <dd>{sync.data.openPullRequestCount ?? t("Not available from a working copy")}</dd>
          </div>
          <div>
            <dt className="text-xs text-[var(--color-muted)]">{t("Open issues")}</dt>
            <dd>{sync.data.openIssueCount ?? t("Not available from a working copy")}</dd>
          </div>
        </dl>
      ) : null}
    </Panel>
  );
}
