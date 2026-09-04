import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { useSession, useWorkspace } from "../api/session";
import { Alert, Badge, Button, Field, Input, Panel, Table } from "../components/ui";
import { Failure } from "../components/Failure";

/**
 * Standing up another workspace, sponsored by this one.
 *
 * There is no installation-wide superuser in this system and this screen does not imply one: the
 * permission is held on the workspace you are looking at, the request is authorised against it,
 * and you become the administrator of whatever you create. A person who administers nothing
 * creates nothing.
 */
export function WorkspaceProvisioning() {
  const { workspaceId } = useParams();
  const { user, reload } = useSession();
  const access = useWorkspace(workspaceId);

  const [name, setName] = useState("");
  const [firstProjectName, setFirstProjectName] = useState("");

  const create = useMutation({
    mutationFn: () =>
      invoke("create_workspace", {
        sponsorWorkspaceId: workspaceId!,
        name,
        firstProjectName: firstProjectName.trim() === "" ? null : firstProjectName,
      }),
    onSuccess: async () => {
      setName("");
      setFirstProjectName("");

      // The new workspace comes with a membership, so the session's own list is now out of date.
      await reload();
    },
  });

  const workspaces = [
    ...new Map((user?.workspaces ?? []).map((entry) => [entry.workspaceId, entry])).values(),
  ];

  return (
    <>
      <Panel title="Workspaces you can reach">
        <Table head={["Workspace", "Role", ""]}>
          {workspaces.map((entry) => (
            <tr key={entry.workspaceId} className="border-b border-[var(--color-line)] last:border-0">
              <td className="px-2 py-2">
                <Link className="underline" to={`/w/${entry.workspaceId}`}>
                  {entry.name}
                </Link>
              </td>
              <td className="px-2 py-2">
                <Badge tone={entry.workspaceId === workspaceId ? "live" : "neutral"}>{entry.role}</Badge>
              </td>
              <td className="px-2 py-2 text-right text-xs text-[var(--color-muted)]">
                {entry.workspaceId === workspaceId ? "You are here" : ""}
              </td>
            </tr>
          ))}
        </Table>

        <p className="mt-3 text-xs text-[var(--color-muted)]">
          Every workspace is a separate tenant. Nothing is shared between them — not projects, not
          records, not evidence — and a grant on one says nothing about any other.
        </p>
      </Panel>

      <Panel title="New workspace">
        <form
          className="grid gap-3 sm:grid-cols-2"
          onSubmit={(event) => {
            event.preventDefault();
            create.mutate();
          }}
        >
          <Field label="Name">
            <Input
              required
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="Northwind"
            />
          </Field>

          <Field label="First project" hint="Optional. One can be created inside it afterwards instead.">
            <Input
              value={firstProjectName}
              onChange={(event) => setFirstProjectName(event.target.value)}
              placeholder="Payments platform"
            />
          </Field>

          <div className="flex items-end">
            <Button type="submit" variant="primary" disabled={create.isPending}>
              Create workspace
            </Button>
          </div>
        </form>

        {create.isError ? (
          <div className="mt-3">
            <Failure error={create.error} />
          </div>
        ) : null}

        {create.isSuccess ? (
          <div className="mt-3">
            <Alert tone="success">
              Created, sponsored by {access?.name ?? "this workspace"}. You administer it —{" "}
              <Link className="underline" to={`/w/${create.data.workspaceId}`}>
                open {create.data.name}
              </Link>
              .
            </Alert>
          </div>
        ) : null}

        <p className="mt-3 text-xs text-[var(--color-muted)]">
          Sponsored by {access?.name ?? "this workspace"}: the permission that allows this is the
          one you hold here, and you become the new workspace's administrator. Nobody else is
          carried over.
        </p>
      </Panel>
    </>
  );
}
