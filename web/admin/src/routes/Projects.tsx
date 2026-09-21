import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { grants, useWorkspace } from "../api/session";
import { Alert, Badge, Button, Empty, Field, Input, Panel, Table } from "../components/ui";
import { Failure } from "../components/Failure";

/**
 * Projects in a workspace, and the per-project AI access policy.
 *
 * The policy is the most consequential switch in the product, so it is shown as state rather than
 * as a toggle whose position you have to infer: a project is either enabled, with who enabled it
 * and when, or it says nothing reaches AI. Off is the default and stays the default; a project
 * nobody has configured behaves exactly like one configured to deny.
 */
export function Projects() {
  const { workspaceId } = useParams();
  const access = useWorkspace(workspaceId);
  const queries = useQueryClient();
  const [name, setName] = useState("");

  const projects = useQuery({
    queryKey: ["projects", workspaceId],
    queryFn: () => invoke("list_projects", { workspaceId: workspaceId! }),
    enabled: Boolean(workspaceId),
  });

  const create = useMutation({
    mutationFn: (projectName: string) =>
      invoke("create_project", { workspaceId: workspaceId!, name: projectName }),
    onSuccess: async () => {
      setName("");
      await queries.invalidateQueries({ queryKey: ["projects", workspaceId] });
    },
  });

  const canCreate = grants(access, "ManageProjects");
  const canManageAi = grants(access, "ManageAccess");

  return (
    <>
      <Panel title="Projects">
        {projects.isPending ? (
          <Empty>Loading…</Empty>
        ) : projects.isError ? (
          <Failure error={projects.error} />
        ) : projects.data.projects.length === 0 ? (
          <Empty>
            No projects yet.
            {canCreate ? " Create one below." : " An administrator has to create one."}
          </Empty>
        ) : (
          <Table head={["Project", "AI access", ""]}>
            {projects.data.projects.map((project) => (
              <tr key={project.projectId} className="border-b border-[var(--color-line)] last:border-0">
                <td className="px-2 py-2">
                  <Link className="underline" to={`p/${project.projectId}`}>
                    {project.name}
                  </Link>
                </td>
                <td className="px-2 py-2">
                  {project.aiAccessEnabled ? (
                    <Badge tone="live">Enabled</Badge>
                  ) : (
                    <Badge>Denied</Badge>
                  )}
                  {project.aiScopeUnstructured ? (
                    <p className="mt-1 text-xs text-[var(--color-danger)]">
                      Approved before rules could be named: every personal-data rule is off for AI.
                    </p>
                  ) : project.aiAllowedPersonalDataRules && project.aiAllowedPersonalDataRules.length > 0 ? (
                    <p className="mt-1 text-xs text-[var(--color-muted)]" aria-label={`Bounded scope of ${project.name}`}>
                      AI may see: {project.aiAllowedPersonalDataRules.join(", ")}
                    </p>
                  ) : null}
                </td>
                <td className="px-2 py-2 text-right">
                  <span className="inline-flex items-center gap-2">
                    {canManageAi ? (
                      <AiAccessButton
                        workspaceId={workspaceId!}
                        projectId={project.projectId}
                        enabled={project.aiAccessEnabled}
                      />
                    ) : null}
                    {canManageAi && project.aiAccessEnabled ? (
                      <BoundedScopeButton
                        workspaceId={workspaceId!}
                        projectId={project.projectId}
                        name={project.name}
                        allowed={project.aiAllowedPersonalDataRules ?? []}
                      />
                    ) : null}
                    {canCreate ? (
                      <DeleteProjectButton
                        workspaceId={workspaceId!}
                        projectId={project.projectId}
                        name={project.name}
                      />
                    ) : null}
                  </span>
                </td>
              </tr>
            ))}
          </Table>
        )}
      </Panel>

      {canCreate ? (
        <Panel title="New project">
          <form
            className="flex items-end gap-3"
            onSubmit={(event) => {
              event.preventDefault();
              create.mutate(name);
            }}
          >
            <div className="flex-1">
              <Field label="Name">
                <Input
                  required
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                  placeholder="Payments platform"
                />
              </Field>
            </div>
            <Button type="submit" variant="primary" disabled={create.isPending}>
              Create
            </Button>
          </form>

          {create.isError ? (
            <div className="mt-3">
              <Failure error={create.error} />
            </div>
          ) : null}

          <p className="mt-3 text-xs text-[var(--color-muted)]">
            A new project is closed to AI until somebody opens it, and stays closed if nobody does.
          </p>
        </Panel>
      ) : null}
    </>
  );
}

/**
 * Deleting a project takes its records, their whole revision history, and the evidence bytes
 * behind them, immediately and with no undo. So it asks for the name to be typed: a confirmation
 * dialogue teaches somebody to click through it, and typing the name is the one confirmation that
 * cannot be given by accident.
 */
function DeleteProjectButton({
  workspaceId,
  projectId,
  name,
}: {
  workspaceId: string;
  projectId: string;
  name: string;
}) {
  const queries = useQueryClient();
  const [confirming, setConfirming] = useState(false);
  const [typed, setTyped] = useState("");

  const remove = useMutation({
    mutationFn: () => invoke("delete_project", { scope: { workspaceId, projectId } }),
    onSuccess: async () => {
      setConfirming(false);
      setTyped("");
      await queries.invalidateQueries({ queryKey: ["projects", workspaceId] });
    },
  });

  if (!confirming) {
    return (
      <Button variant="danger" onClick={() => setConfirming(true)}>
        Delete
      </Button>
    );
  }

  return (
    <span className="inline-flex flex-col items-end gap-1">
      <form
        className="flex items-center gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          remove.mutate();
        }}
      >
        <Input
          required
          aria-label={`Type ${name} to confirm deletion`}
          placeholder={name}
          value={typed}
          onChange={(event) => setTyped(event.target.value)}
          className="w-40"
        />
        <Button type="submit" variant="danger" disabled={typed !== name || remove.isPending}>
          Delete for good
        </Button>
        <Button
          onClick={() => {
            setConfirming(false);
            setTyped("");
          }}
        >
          Cancel
        </Button>
      </form>

      <span className="text-xs text-[var(--color-muted)]">
        Records, history, and evidence go with it. Audit history stays.
      </span>

      {remove.isError ? <Failure error={remove.error} /> : null}
    </span>
  );
}

const PERSONAL_DATA_RULES = [
  ["email-address", "Email addresses"],
  ["us-ssn", "US social security numbers"],
  ["payment-card-number", "Payment card numbers"],
  ["thai-national-id", "Thai national ID numbers"],
  ["thai-mobile-number", "Thai mobile numbers"],
  ["labelled-personal-data", "Labelled personal details (date of birth, ID, address)"],
] as const;

/**
 * Approves, changes or withdraws a bounded data scope for one project (SB-18, Phase 13 B9).
 *
 * By default the AI channel never sees personal data: it is blocked from drafts and redacted from
 * reads. A scope names the rules an assistant may see through on this project, and why. It is a
 * separate step from enabling access, names every rule it allows before it is confirmed, and can
 * be withdrawn. A secret is refused whatever the scope says.
 */
function BoundedScopeButton({
  workspaceId,
  projectId,
  name,
  allowed,
}: {
  workspaceId: string;
  projectId: string;
  name: string;
  allowed: string[];
}) {
  const queries = useQueryClient();
  const [open, setOpen] = useState(false);
  const [rules, setRules] = useState<string[]>(allowed);
  const [justification, setJustification] = useState("");
  const [confirmed, setConfirmed] = useState(false);

  const approve = useMutation({
    mutationFn: (withdraw: boolean) =>
      invoke("enable_project_ai_access", {
        scope: { workspaceId, projectId },
        allowedPersonalDataRules: withdraw ? null : rules,
        justification: withdraw ? null : justification,
      }),
    onSuccess: async () => {
      setOpen(false);
      setConfirmed(false);
      await queries.invalidateQueries({ queryKey: ["projects", workspaceId] });
    },
  });

  if (!open) {
    return <Button onClick={() => setOpen(true)}>Bounded scope</Button>;
  }

  const labels = PERSONAL_DATA_RULES.filter(([rule]) => rules.includes(rule)).map(([, label]) => label);

  return (
    <div className="max-w-md space-y-2 text-left" aria-label={`Bounded scope for ${name}`}>
      <p className="text-xs text-[var(--color-muted)]">
        Choose what an assistant may see on {name}. Everything not ticked stays blocked and redacted.
      </p>
      {PERSONAL_DATA_RULES.map(([rule, label]) => (
        <label key={rule} className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={rules.includes(rule)}
            onChange={(event) =>
              setRules(event.target.checked ? [...rules, rule] : rules.filter((candidate) => candidate !== rule))
            }
          />
          {label}
        </label>
      ))}
      <Field label="Why is this approved?">
        <Input value={justification} maxLength={1000} onChange={(event) => setJustification(event.target.value)} />
      </Field>
      {rules.length > 0 ? (
        <label className="flex items-start gap-2 text-sm">
          <input type="checkbox" checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} />
          <span>I approve an assistant seeing: {labels.join(", ")}. Secrets stay refused.</span>
        </label>
      ) : null}
      <div className="flex gap-2">
        <Button
          variant="danger"
          disabled={approve.isPending || rules.length === 0 || justification.trim().length < 20 || !confirmed}
          onClick={() => approve.mutate(false)}
        >
          Approve scope
        </Button>
        {allowed.length > 0 ? (
          <Button disabled={approve.isPending} onClick={() => approve.mutate(true)}>
            Withdraw scope
          </Button>
        ) : null}
        <Button onClick={() => setOpen(false)}>Cancel</Button>
      </div>
      {approve.isError ? <Failure error={approve.error} /> : null}
    </div>
  );
}

function AiAccessButton({
  workspaceId,
  projectId,
  enabled,
}: {
  workspaceId: string;
  projectId: string;
  enabled: boolean;
}) {
  const queries = useQueryClient();

  const change = useMutation({
    mutationFn: () =>
      enabled
        ? invoke("disable_project_ai_access", { scope: { workspaceId, projectId } })
        : invoke("enable_project_ai_access", { scope: { workspaceId, projectId } }),
    onSuccess: () => queries.invalidateQueries({ queryKey: ["projects", workspaceId] }),
  });

  return (
    <>
      <Button
        variant={enabled ? "danger" : "secondary"}
        disabled={change.isPending}
        onClick={() => change.mutate()}
      >
        {enabled ? "Deny AI access" : "Enable AI access"}
      </Button>

      {change.isError ? (
        <div className="mt-2">
          <Alert tone="error">{String(change.error)}</Alert>
        </div>
      ) : null}
    </>
  );
}
