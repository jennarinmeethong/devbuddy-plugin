import { useState } from "react";
import { useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { Alert, Badge, Button, Empty, Field, Input, Panel, Table, When } from "../components/ui";
import { Failure } from "../components/Failure";

/**
 * Machine tokens: the credential a locally launched Claude or Codex plugin puts in its
 * configuration.
 *
 * Everybody sees this page, including a viewer, and that is not a loosening. A token carries
 * exactly the permissions its owner already has, so minting one grants nothing new; refusing it
 * would only mean a viewer could read knowledge in a browser and not from the editor they work in.
 *
 * The value is shown once and never again — the server keeps a hash. Losing it means minting
 * another, which is the right trade for a credential that would otherwise sit readable in a
 * database and on a screen.
 *
 * Everything on this page is about the workspace in the route and nothing else. A token is minted
 * for it, listed within it, and revoked inside it, because a token now works in exactly one
 * workspace. Somebody who works in two comes to this page twice, and holds two tokens.
 */
export function PluginAccess() {
  const { workspaceId } = useParams();
  const queries = useQueryClient();
  const [name, setName] = useState("");
  const [days, setDays] = useState(90);

  const tokens = useQuery({
    queryKey: ["machine-tokens", workspaceId],
    queryFn: () => invoke("list_machine_tokens", { workspaceId: workspaceId! }),
    enabled: Boolean(workspaceId),
  });

  const issue = useMutation({
    mutationFn: () =>
      invoke("issue_machine_token", { workspaceId: workspaceId!, name, lifetimeDays: days }),
    onSuccess: async () => {
      setName("");
      await queries.invalidateQueries({ queryKey: ["machine-tokens", workspaceId] });
    },
  });

  const revoke = useMutation({
    mutationFn: (tokenId: string) =>
      invoke("revoke_machine_token", { workspaceId: workspaceId!, tokenId }),
    onSuccess: () => queries.invalidateQueries({ queryKey: ["machine-tokens", workspaceId] }),
  });

  return (
    <>
      <Panel title="Your plugin tokens for this workspace">
        {tokens.isPending ? (
          <Empty>Loading…</Empty>
        ) : tokens.isError ? (
          <Failure error={tokens.error} />
        ) : tokens.data.tokens.length === 0 ? (
          <Empty>You have not issued any. Mint one below to connect Claude or Codex.</Empty>
        ) : (
          <Table head={["Name", "Issued", "Expires", "Last used", "State", ""]}>
            {tokens.data.tokens.map((token) => (
              <tr key={token.id} className="border-b border-[var(--color-line)] last:border-0">
                <td className="px-2 py-2">{token.name}</td>
                <td className="px-2 py-2 text-xs">
                  <When value={token.issuedAt} />
                </td>
                <td className="px-2 py-2 text-xs">
                  <When value={token.expiresAt} />
                </td>
                <td className="px-2 py-2 text-xs">
                  <When value={token.lastUsedAt} />
                </td>
                <td className="px-2 py-2">
                  {token.needsReplacement ? (
                    <Badge tone="muted">Needs replacing</Badge>
                  ) : token.isActive ? (
                    <Badge tone="live">Active</Badge>
                  ) : (
                    <Badge tone="muted">Ended</Badge>
                  )}
                </td>
                <td className="px-2 py-2 text-right">
                  {/* A token needing replacement is already refused everywhere, but it is still a
                      row somebody wants gone once they have minted its successor. */}
                  {token.isActive || token.needsReplacement ? (
                    <Button
                      variant="danger"
                      disabled={revoke.isPending}
                      onClick={() => revoke.mutate(token.id)}
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
          A token acts as you, with your permissions and no more, in this workspace and no other. A
          call naming a different workspace is refused even where you are a member, so if you work
          in more than one, mint a token in each. Revoking one takes effect on the next call, not
          the next restart — and never touches your password.
        </p>

        <p className="mt-2 text-xs text-[var(--color-muted)]">
          The same token works in Claude and in Codex. It identifies you to DevBuddy and is not
          tied to, and does not verify, an account with either of them. A token per assistant is
          worth having only if you want to revoke or watch them separately.
        </p>

        {tokens.data?.tokens.some((token) => token.needsReplacement) ? (
          <div className="mt-3">
            <Alert tone="error">
              A token above was issued before tokens were tied to a workspace, and no longer works
              anywhere. It cannot be repaired — only a hash of it was ever stored — so mint a
              replacement here, put that in your plugin configuration, and revoke the old one.
            </Alert>
          </div>
        ) : null}
      </Panel>

      <Panel title="Mint a token">
        <form
          className="grid gap-3 sm:grid-cols-2"
          onSubmit={(event) => {
            event.preventDefault();
            issue.mutate();
          }}
        >
          <Field
            label="Name"
            hint="Where it will live, so you can tell which to revoke later. The machine is the useful part, not the assistant."
          >
            <Input
              required
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="Work laptop"
            />
          </Field>

          <Field label="Days until it expires" hint="Between 1 and 365.">
            <Input
              type="number"
              min={1}
              max={365}
              value={days}
              onChange={(event) => setDays(Number(event.target.value) || 1)}
            />
          </Field>

          <div className="sm:col-span-2">
            <Button type="submit" variant="primary" disabled={issue.isPending}>
              Mint
            </Button>
          </div>
        </form>

        {issue.isError ? (
          <div className="mt-3">
            <Failure error={issue.error} />
          </div>
        ) : null}

        {issue.isSuccess ? (
          <div className="mt-3 space-y-2">
            <Alert tone="success">
              Copy this now. It is shown once and stored only as a hash, so nobody — including this
              page — can show it to you again.
            </Alert>
            <code className="block break-all rounded bg-neutral-100 p-2 font-mono text-xs">
              {issue.data.token}
            </code>
            <p className="text-xs text-[var(--color-muted)]">
              Expires <When value={issue.data.expiresAt} />. Put it in your plugin configuration as{" "}
              <code className="font-mono">DEVBUDDY_TOKEN</code>, in the environment the assistant is
              launched from rather than in a file shared by every project on the machine.
            </p>
            <p className="text-xs text-[var(--color-muted)]">
              It works in this workspace only. Whichever token is in the environment when the
              assistant starts is the identity every call runs as, and changing folder afterwards
              does not change it — so start a session per workspace.
            </p>
          </div>
        ) : null}
      </Panel>
    </>
  );
}
