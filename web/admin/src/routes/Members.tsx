import { useState } from "react";
import { useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { useWorkspace } from "../api/session";
import { Alert, Badge, Button, Empty, Field, Input, Panel, Select, Table, When } from "../components/ui";
import { Failure } from "../components/Failure";

const ROLES = ["Viewer", "Contributor", "Reviewer", "Administrator"] as const;

/**
 * Membership administration: who is in this workspace, and bringing somebody new in.
 *
 * Creating an account hands back a one-time setup token, shown once and never stored here. The
 * server also emails it when the deployment has SMTP configured, and this screen cannot tell
 * whether it did — so it shows the token either way rather than claiming a delivery it cannot
 * confirm.
 */
export function Members() {
  const { workspaceId } = useParams();
  const access = useWorkspace(workspaceId);
  const queries = useQueryClient();

  const memberships = useQuery({
    queryKey: ["memberships", workspaceId],
    queryFn: () => invoke("list_memberships", { workspaceId: workspaceId! }),
    enabled: Boolean(workspaceId),
  });

  const revoke = useMutation({
    mutationFn: (membershipId: string) =>
      invoke("revoke_membership", { workspaceId: workspaceId!, membershipId }),
    onSuccess: () => queries.invalidateQueries({ queryKey: ["memberships", workspaceId] }),
  });

  return (
    <>
      <Panel title={`Members of ${access?.name ?? "this workspace"}`}>
        {memberships.isPending ? (
          <Empty>Loading…</Empty>
        ) : memberships.isError ? (
          <Failure error={memberships.error} />
        ) : memberships.data.memberships.length === 0 ? (
          <Empty>No memberships.</Empty>
        ) : (
          <Table head={["User", "Role", "Scope", "Granted", "State", ""]}>
            {memberships.data.memberships.map((membership) => (
              <tr
                key={membership.membershipId}
                className="border-b border-[var(--color-line)] last:border-0"
              >
                <td className="px-2 py-2 font-mono text-xs">{membership.userId}</td>
                <td className="px-2 py-2">{membership.role}</td>
                <td className="px-2 py-2 text-xs text-[var(--color-muted)]">
                  {membership.scopedToProject ? `Project ${membership.scopedToProject}` : "Whole workspace"}
                </td>
                <td className="px-2 py-2 text-xs">
                  <When value={membership.grantedAt} />
                </td>
                <td className="px-2 py-2">
                  {membership.isActive ? <Badge tone="live">Active</Badge> : <Badge tone="muted">Revoked</Badge>}
                </td>
                <td className="px-2 py-2 text-right">
                  {membership.isActive ? (
                    <Button
                      variant="danger"
                      disabled={revoke.isPending}
                      onClick={() => revoke.mutate(membership.membershipId)}
                    >
                      Revoke
                    </Button>
                  ) : null}
                </td>
              </tr>
            ))}
          </Table>
        )}

        {revoke.isError ? (
          <div className="mt-3">
            <Failure error={revoke.error} />
          </div>
        ) : null}

        <p className="mt-3 text-xs text-[var(--color-muted)]">
          Revoked grants stay listed. Hiding them would make a revocation look like the grant never
          happened, and a revocation takes effect on the next request rather than the next sign-in.
        </p>
      </Panel>

      <InviteForm workspaceId={workspaceId!} />
    </>
  );
}

function InviteForm({ workspaceId }: { workspaceId: string }) {
  const queries = useQueryClient();
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [role, setRole] = useState<(typeof ROLES)[number]>("Viewer");

  const create = useMutation({
    mutationFn: () => invoke("create_user_account", { workspaceId, email, displayName, role }),
    onSuccess: async () => {
      setEmail("");
      setDisplayName("");
      await queries.invalidateQueries({ queryKey: ["memberships", workspaceId] });
    },
  });

  return (
    <Panel title="Add somebody">
      <form
        className="grid gap-3 sm:grid-cols-2"
        onSubmit={(event) => {
          event.preventDefault();
          create.mutate();
        }}
      >
        <Field label="Email">
          <Input
            type="email"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </Field>

        <Field label="Display name">
          <Input
            required
            value={displayName}
            onChange={(event) => setDisplayName(event.target.value)}
          />
        </Field>

        <Field label="Role" hint="Roles are cumulative: a reviewer can do everything a contributor can.">
          <Select value={role} onChange={(event) => setRole(event.target.value as (typeof ROLES)[number])}>
            {ROLES.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </Select>
        </Field>

        <div className="flex items-end">
          <Button type="submit" variant="primary" disabled={create.isPending}>
            Create account
          </Button>
        </div>
      </form>

      {create.isError ? (
        <div className="mt-3">
          <Failure error={create.error} />
        </div>
      ) : null}

      {create.isSuccess ? (
        <div className="mt-3 space-y-2">
          <Alert tone="success">
            The account exists and cannot be signed into until its owner sets a password. Give them
            this setup token — it is shown once, is single-use, and expires.
          </Alert>
          <code className="block break-all rounded bg-neutral-100 p-2 font-mono text-xs">
            {create.data.setupToken}
          </code>
          <p className="text-xs text-[var(--color-muted)]">
            Expires <When value={create.data.setupTokenExpiresAt} />. It is also emailed to that
            address when the deployment has SMTP configured; when it does not, this is the copy.
          </p>
        </div>
      ) : null}
    </Panel>
  );
}
