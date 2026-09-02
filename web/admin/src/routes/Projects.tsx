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
                </td>
                <td className="px-2 py-2 text-right">
                  {canManageAi ? (
                    <AiAccessButton
                      workspaceId={workspaceId!}
                      projectId={project.projectId}
                      enabled={project.aiAccessEnabled}
                    />
                  ) : null}
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
