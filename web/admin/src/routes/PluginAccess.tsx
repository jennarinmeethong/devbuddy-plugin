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
      <Panel title="Your plugin tokens">
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
                  {token.isActive ? <Badge tone="live">Active</Badge> : <Badge tone="muted">Ended</Badge>}
                </td>
                <td className="px-2 py-2 text-right">
                  {token.isActive ? (
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
          A token acts as you, with your permissions and no more. Revoking one takes effect on the
          next call, not the next restart — and never touches your password.
        </p>
      </Panel>

      <Panel title="Mint a token">
        <form
          className="grid gap-3 sm:grid-cols-2"
          onSubmit={(event) => {
            event.preventDefault();
            issue.mutate();
          }}
        >
          <Field label="Name" hint="What it is for, so you can tell which to revoke later.">
            <Input
              required
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="Work laptop, Codex"
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
              <code className="font-mono">DEVBUDDY_TOKEN</code>.
            </p>
          </div>
        ) : null}
      </Panel>
    </>
  );
}
