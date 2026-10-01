import { useState } from "react";
import { useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { refetchAfterWrite } from "../api/queries";
import { Alert, Badge, Button, Empty, Field, Input, Panel, Table, When } from "../components/ui";
import { Failure } from "../components/Failure";
import { t, tr } from "../i18n";
import { Hint, usePageTour } from "../components/Guide";
import { HINTS, TOURS } from "../guide/content";

/**
 * Machine tokens: the credential Claude Code and Codex present to the MCP server, which the
 * `devbuddy` client keeps per checkout in the operating system's credential store (Phase 14, A4;
 * ADR-0015). The page ends with the command that registers a checkout, with this server's address
 * and this workspace's identifiers filled in, because nothing else on screen shows them.
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

  usePageTour(TOURS.pluginAccess);

  const projects = useQuery({
    queryKey: ["projects", workspaceId],
    queryFn: () => invoke("list_projects", { workspaceId: workspaceId! }),
    enabled: Boolean(workspaceId),
  });

  // The address this page came from is the gateway's, and the MCP endpoint is on it at /mcp.
  const register = `devbuddy register --server ${window.location.origin} --workspace ${workspaceId}`;

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
      await refetchAfterWrite(queries, { queryKey: ["machine-tokens", workspaceId] });
    },
  });

  const revoke = useMutation({
    mutationFn: (tokenId: string) =>
      invoke("revoke_machine_token", { workspaceId: workspaceId!, tokenId }),
    onSuccess: () => refetchAfterWrite(queries, { queryKey: ["machine-tokens", workspaceId] }),
  });

  return (
    <>
      <Panel title={t("Your plugin tokens for this workspace")} tour="plugin-tokens" hint={<Hint topic={HINTS.machineToken} />}>
        {tokens.isPending ? (
          <Empty>{t("Loading…")}</Empty>
        ) : tokens.isError ? (
          <Failure error={tokens.error} />
        ) : tokens.data.tokens.length === 0 ? (
          <Empty>{t("You have not issued any. Mint one below to connect Claude or Codex.")}</Empty>
        ) : (
          <Table head={[t("Name"), t("Issued"), t("Expires"), t("Last used"), t("State"), ""]}>
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
                    <Badge tone="muted">{t("Needs replacing")}</Badge>
                  ) : token.isActive ? (
                    <Badge tone="live">{t("Active")}</Badge>
                  ) : (
                    <Badge tone="muted">{t("Ended")}</Badge>
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
                      {t("Revoke")}
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
          {t("A token acts as you, with your permissions and no more, in this workspace and no other. A call naming a different workspace is refused even where you are a member, so if you work in more than one, mint a token in each. Revoking one takes effect on the next call, not the next restart — and never touches your password.")}
        </p>

        <p className="mt-2 text-xs text-[var(--color-muted)]">
          {t("The same token works in Claude and in Codex. It identifies you to DevBuddy and is not tied to, and does not verify, an account with either of them. A token per assistant is worth having only if you want to revoke or watch them separately.")}
        </p>

        {tokens.data?.tokens.some((token) => token.needsReplacement) ? (
          <div className="mt-3">
            <Alert tone="error">
              {t("A token above was issued before tokens were tied to a workspace, and no longer works anywhere. It cannot be repaired — only a hash of it was ever stored — so mint a replacement here, register your checkouts with it, and revoke the old one.")}
            </Alert>
          </div>
        ) : null}
      </Panel>

      <Panel title={t("Mint a token")} tour="plugin-mint">
        <form
          className="grid gap-3 sm:grid-cols-2"
          onSubmit={(event) => {
            event.preventDefault();
            issue.mutate();
          }}
        >
          <Field
            label={t("Name")}
            hint={t("Where it will live, so you can tell which to revoke later. The machine is the useful part, not the assistant.")}
          >
            <Input
              required
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder={t("Work laptop")}
            />
          </Field>

          <Field label={t("Days until it expires")} hint={t("Between 1 and 365.")}>
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
              {t("Mint")}
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
              {t("Copy this now. It is shown once and stored only as a hash, so nobody — including this page — can show it to you again.")}
            </Alert>
            <code className="block break-all rounded bg-[var(--color-soft)] p-2 font-mono text-xs">
              {issue.data.token}
            </code>
            <p className="text-xs text-[var(--color-muted)]">
              {tr(
                "Expires {when}. Register a checkout with it using the command below: the devbuddy client asks for the token once per workspace and keeps it in your operating system's credential store. Never put it in a configuration file.",
                { when: <When value={issue.data.expiresAt} /> },
              )}
            </p>
            <p className="text-xs text-[var(--color-muted)]">
              {t("It works in this workspace only. A session is one workspace, the one the checkout it was started in is registered to, and changing folder afterwards does not change it.")}
            </p>
          </div>
        ) : null}
      </Panel>

      <Panel title={t("Connect a checkout")} tour="plugin-connect" hint={<Hint topic={HINTS.registerCheckout} />}>
        <p className="text-sm">
          {t("In the checkout, on the machine where Claude Code or Codex runs, with the devbuddy client installed:")}
        </p>
        <code className="mt-2 block break-all rounded bg-[var(--color-soft)] p-2 font-mono text-xs">
          {register}
        </code>
        <p className="mt-2 text-xs text-[var(--color-muted)]">
          {t("Add a project to make it the assistants' default. A project closed to AI is not listed to them at all.")}
        </p>

        {projects.isError ? (
          <div className="mt-3">
            <Failure error={projects.error} />
          </div>
        ) : projects.data && projects.data.projects.length > 0 ? (
          <div className="mt-3">
            <Table head={[t("Project"), t("AI access"), t("Command")]}>
              {projects.data.projects.map((project) => (
                <tr key={project.projectId} className="border-b border-[var(--color-line)] last:border-0">
                  <td className="px-2 py-2">{project.name}</td>
                  <td className="px-2 py-2">
                    {project.aiAccessEnabled ? (
                      <Badge tone="live">{t("Enabled")}</Badge>
                    ) : (
                      <Badge>{t("Denied")}</Badge>
                    )}
                  </td>
                  <td className="px-2 py-2">
                    <code className="break-all font-mono text-xs">{`${register} --project ${project.projectId}`}</code>
                  </td>
                </tr>
              ))}
            </Table>
          </div>
        ) : null}

        <p className="mt-3 text-xs text-[var(--color-muted)]">
          {t("Then run devbuddy doctor there. It checks the registration, the stored token, this server's certificate and that the token works in this workspace.")}
        </p>
      </Panel>
    </>
  );
}
