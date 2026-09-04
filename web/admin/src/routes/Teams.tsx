import { useState } from "react";
import { useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { useWorkspace } from "../api/session";
import { Alert, Button, Empty, Field, Input, Panel, Select, Table } from "../components/ui";
import { Failure } from "../components/Failure";

/**
 * Team administration: who is grouped with whom inside this workspace.
 *
 * A team carries no permission of its own, and the screen says so rather than leaving somebody to
 * assume otherwise. Membership decides what a person may do; adding them to a team changes nothing
 * about their access, which is exactly why this screen is separate from Members.
 */
export function Teams() {
  const { workspaceId } = useParams();
  const access = useWorkspace(workspaceId);
  const queries = useQueryClient();
  const [name, setName] = useState("");
  const [open, setOpen] = useState<string | null>(null);

  const teams = useQuery({
    queryKey: ["teams", workspaceId],
    queryFn: () => invoke("list_teams", { workspaceId: workspaceId! }),
    enabled: Boolean(workspaceId),
  });

  const create = useMutation({
    mutationFn: (teamName: string) => invoke("create_team", { workspaceId: workspaceId!, name: teamName }),
    onSuccess: async () => {
      setName("");
      await queries.invalidateQueries({ queryKey: ["teams", workspaceId] });
    },
  });

  return (
    <>
      <Panel title={`Teams in ${access?.name ?? "this workspace"}`}>
        {teams.isPending ? (
          <Empty>Loading…</Empty>
        ) : teams.isError ? (
          <Failure error={teams.error} />
        ) : teams.data.teams.length === 0 ? (
          <Empty>No teams yet. Create one below.</Empty>
        ) : (
          <Table head={["Team", "Members", ""]}>
            {teams.data.teams.map((team) => (
              <TeamRow
                key={team.teamId}
                workspaceId={workspaceId!}
                teamId={team.teamId}
                name={team.name}
                open={open === team.teamId}
                onToggle={() => setOpen(open === team.teamId ? null : team.teamId)}
              />
            ))}
          </Table>
        )}

        <p className="mt-3 text-xs text-[var(--color-muted)]">
          A team is a grouping and nothing more. It carries no role and no permission — membership
          decides what anybody may do, whether or not they are on a team.
        </p>
      </Panel>

      <Panel title="New team">
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
                placeholder="Platform"
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
      </Panel>
    </>
  );
}

function TeamRow({
  workspaceId,
  teamId,
  name,
  open,
  onToggle,
}: {
  workspaceId: string;
  teamId: string;
  name: string;
  open: boolean;
  onToggle: () => void;
}) {
  const queries = useQueryClient();
  const [renaming, setRenaming] = useState(false);
  const [draft, setDraft] = useState(name);
  const [confirming, setConfirming] = useState(false);

  const invalidate = () => queries.invalidateQueries({ queryKey: ["teams", workspaceId] });

  const rename = useMutation({
    mutationFn: () => invoke("rename_team", { workspaceId, teamId, name: draft }),
    onSuccess: async () => {
      setRenaming(false);
      await invalidate();
    },
  });

  const remove = useMutation({
    mutationFn: () => invoke("delete_team", { workspaceId, teamId }),
    onSuccess: async () => {
      setConfirming(false);
      await invalidate();
    },
  });

  return (
    <>
      <tr className="border-b border-[var(--color-line)] last:border-0">
        <td className="px-2 py-2">
          {renaming ? (
            <form
              className="flex items-center gap-2"
              onSubmit={(event) => {
                event.preventDefault();
                rename.mutate();
              }}
            >
              <Input
                required
                aria-label={`New name for ${name}`}
                value={draft}
                onChange={(event) => setDraft(event.target.value)}
              />
              <Button type="submit" variant="primary" disabled={rename.isPending}>
                Save
              </Button>
              <Button
                onClick={() => {
                  setRenaming(false);
                  setDraft(name);
                }}
              >
                Cancel
              </Button>
            </form>
          ) : (
            <button type="button" className="text-sm underline" onClick={onToggle}>
              {name}
            </button>
          )}
        </td>

        <td className="px-2 py-2 text-xs text-[var(--color-muted)]">
          {open ? "Members shown below" : ""}
        </td>

        <td className="px-2 py-2 text-right">
          {confirming ? (
            <span className="inline-flex items-center gap-2">
              <span className="text-xs text-[var(--color-muted)]">Delete this team?</span>
              <Button variant="danger" disabled={remove.isPending} onClick={() => remove.mutate()}>
                Confirm
              </Button>
              <Button onClick={() => setConfirming(false)}>Cancel</Button>
            </span>
          ) : renaming ? null : (
            <span className="inline-flex items-center gap-2">
              <Button onClick={onToggle}>{open ? "Hide members" : "Members"}</Button>
              <Button onClick={() => setRenaming(true)}>Rename</Button>
              <Button variant="danger" onClick={() => setConfirming(true)}>
                Delete
              </Button>
            </span>
          )}
        </td>
      </tr>

      {rename.isError || remove.isError ? (
        <tr>
          <td colSpan={3} className="px-2 pb-2">
            <Failure error={rename.error ?? remove.error} />
          </td>
        </tr>
      ) : null}

      {open ? (
        <tr>
          <td colSpan={3} className="bg-neutral-50 px-2 py-3">
            <TeamMembers workspaceId={workspaceId} teamId={teamId} teamName={name} />
          </td>
        </tr>
      ) : null}
    </>
  );
}

function TeamMembers({
  workspaceId,
  teamId,
  teamName,
}: {
  workspaceId: string;
  teamId: string;
  teamName: string;
}) {
  const queries = useQueryClient();
  const [userId, setUserId] = useState("");

  const members = useQuery({
    queryKey: ["team-members", workspaceId, teamId],
    queryFn: () => invoke("list_team_members", { workspaceId, teamId }),
  });

  // The people who could be added: everybody with a live grant on this workspace. Offering a text
  // box for an identifier would make a typo indistinguishable from an account that does not exist.
  const memberships = useQuery({
    queryKey: ["memberships", workspaceId],
    queryFn: () => invoke("list_memberships", { workspaceId }),
  });

  const invalidate = () => queries.invalidateQueries({ queryKey: ["team-members", workspaceId, teamId] });

  const add = useMutation({
    mutationFn: () => invoke("add_team_member", { workspaceId, teamId, userId }),
    onSuccess: async () => {
      setUserId("");
      await invalidate();
    },
  });

  const remove = useMutation({
    mutationFn: (member: string) => invoke("remove_team_member", { workspaceId, teamId, userId: member }),
    onSuccess: () => invalidate(),
  });

  const inTeam = new Set((members.data?.members ?? []).map((member) => member.userId));

  const candidates = [
    ...new Map(
      (memberships.data?.memberships ?? [])
        .filter((membership) => membership.isActive && !inTeam.has(membership.userId))
        .map((membership) => [membership.userId, membership]),
    ).values(),
  ];

  return (
    <div className="space-y-3">
      <h3 className="text-sm font-medium">Members of {teamName}</h3>

      {members.isPending ? (
        <Empty>Loading…</Empty>
      ) : members.isError ? (
        <Failure error={members.error} />
      ) : members.data.members.length === 0 ? (
        <Empty>Nobody is on this team yet.</Empty>
      ) : (
        <ul className="divide-y divide-[var(--color-line)] border-y border-[var(--color-line)]">
          {members.data.members.map((member) => (
            <li key={member.userId} className="flex items-center justify-between gap-3 py-2">
              <span className="font-mono text-xs">{member.userId}</span>
              <Button
                variant="danger"
                disabled={remove.isPending}
                onClick={() => remove.mutate(member.userId)}
              >
                Remove
              </Button>
            </li>
          ))}
        </ul>
      )}

      <form
        className="flex items-end gap-3"
        onSubmit={(event) => {
          event.preventDefault();
          add.mutate();
        }}
      >
        <div className="flex-1">
          <Field label="Add somebody">
            <Select
              required
              aria-label={`Add somebody to ${teamName}`}
              value={userId}
              onChange={(event) => setUserId(event.target.value)}
            >
              <option value="">Choose a member of this workspace…</option>
              {candidates.map((membership) => (
                <option key={membership.userId} value={membership.userId}>
                  {membership.userId} ({membership.role})
                </option>
              ))}
            </Select>
          </Field>
        </div>
        <Button type="submit" variant="primary" disabled={add.isPending || userId === ""}>
          Add
        </Button>
      </form>

      {memberships.isError ? (
        // Said plainly rather than as an empty picker: "nobody left to add" and "the list could
        // not be read" look identical from a disabled dropdown, and they are not the same thing.
        <Failure error={memberships.error} />
      ) : candidates.length === 0 && !memberships.isPending ? (
        <Alert>
          Everybody with a grant on this workspace is already on this team. Somebody has to have an
          account and a membership before they can join one.
        </Alert>
      ) : null}

      {add.isError ? <Failure error={add.error} /> : null}
      {remove.isError ? <Failure error={remove.error} /> : null}
    </div>
  );
}
