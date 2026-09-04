import { useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { captureEvidence, downloadEvidence, invoke } from "../api/client";
import { grants, useWorkspace } from "../api/session";
import { Button, Empty, Field, Input, Panel, Table, When } from "../components/ui";
import { Failure } from "../components/Failure";

/**
 * Artefacts attached to a project: a log, a screenshot, an export.
 *
 * The store had a read side and no write side until this screen existed — evidence could be
 * downloaded, backed up, restored and swept, but nothing a person could reach ever put anything
 * in it, so no installation could have had anything to download.
 */
export function Evidence() {
  const { workspaceId, projectId } = useParams();
  const access = useWorkspace(workspaceId);
  const scope = { workspaceId: workspaceId!, projectId: projectId! };

  const evidence = useQuery({
    queryKey: ["evidence", workspaceId, projectId],
    queryFn: () => invoke("list_evidence", { scope }),
    enabled: Boolean(workspaceId && projectId),
  });

  return (
    <>
      <div className="flex items-center justify-between">
        <h1 className="text-lg font-semibold">Evidence</h1>
        <Link className="text-sm underline" to="..">
          Work items
        </Link>
      </div>

      <Panel title="Attached to this project">
        {evidence.isPending ? (
          <Empty>Loading…</Empty>
        ) : evidence.isError ? (
          <Failure error={evidence.error} />
        ) : evidence.data.evidence.length === 0 ? (
          <Empty>Nothing attached yet.</Empty>
        ) : (
          <Table head={["Type", "Size", "Captured", "State", ""]}>
            {evidence.data.evidence.map((item) => (
              <tr
                key={item.evidenceId}
                className="border-b border-[var(--color-line)] last:border-0"
              >
                <td className="px-2 py-2 font-mono text-xs">{item.mediaType}</td>
                <td className="px-2 py-2 text-xs">{sizeOf(item.sizeBytes)}</td>
                <td className="px-2 py-2 text-xs">
                  <When value={item.capturedAt} />
                </td>
                <td className="px-2 py-2 text-xs">{item.redactionState}</td>
                <td className="px-2 py-2 text-right">
                  <DownloadButton
                    scope={scope}
                    evidenceId={item.evidenceId}
                    disabled={!item.isReleasable}
                  />
                </td>
              </tr>
            ))}
          </Table>
        )}
      </Panel>

      {grants(access, "CreateDraft") ? <Attach scope={scope} /> : null}
    </>
  );
}

/**
 * Fetched with the session token and handed to the browser, rather than linked to.
 *
 * There is no shareable evidence URL in this system on purpose: an attachment is reachable only
 * through an authorised request, which is what puts it under the same isolation as the records.
 */
function DownloadButton({
  scope,
  evidenceId,
  disabled,
}: {
  scope: { workspaceId: string; projectId: string };
  evidenceId: string;
  disabled: boolean;
}) {
  const download = useMutation({
    mutationFn: async () => {
      const blob = await downloadEvidence(scope, evidenceId);
      const url = URL.createObjectURL(blob);

      try {
        const link = document.createElement("a");
        link.href = url;
        link.download = evidenceId;
        link.click();
      } finally {
        URL.revokeObjectURL(url);
      }
    },
  });

  return (
    <>
      <Button
        type="button"
        disabled={disabled || download.isPending}
        onClick={() => download.mutate()}
      >
        {download.isPending ? "Fetching…" : "Download"}
      </Button>
      {download.isError ? <Failure error={download.error} /> : null}
    </>
  );
}

function Attach({ scope }: { scope: { workspaceId: string; projectId: string } }) {
  const queries = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const [description, setDescription] = useState("");
  const [file, setFile] = useState<File | null>(null);

  const attach = useMutation({
    mutationFn: () => captureEvidence(scope, file!, description),
    onSuccess: async () => {
      setDescription("");
      setFile(null);

      if (input.current) {
        input.current.value = "";
      }

      await queries.invalidateQueries({
        queryKey: ["evidence", scope.workspaceId, scope.projectId],
      });
    },
  });

  return (
    <Panel title="Attach an artefact">
      <form
        className="grid gap-3"
        onSubmit={(event) => {
          event.preventDefault();
          attach.mutate();
        }}
      >
        <Field label="File">
          <input
            ref={input}
            type="file"
            className="w-full text-sm"
            onChange={(event) => setFile(event.target.files?.[0] ?? null)}
          />
        </Field>

        <Field label="What it shows">
          <Input
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            placeholder="The build log for release 1.4"
          />
        </Field>

        {/* Said here rather than discovered from a refusal: the server scans the file before it
            stores anything, so a log with a key in it comes back refused and nothing is kept. */}
        <p className="text-xs text-[var(--color-muted)]">
          The file is scanned before it is stored. One carrying a credential is refused, and
          nothing is kept.
        </p>

        {attach.isError ? <Failure error={attach.error} /> : null}

        <div>
          <Button type="submit" disabled={!file || description.trim() === "" || attach.isPending}>
            {attach.isPending ? "Attaching…" : "Attach"}
          </Button>
        </div>
      </form>
    </Panel>
  );
}

function sizeOf(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }

  if (bytes < 1024 * 1024) {
    return `${Math.round(bytes / 1024)} KB`;
  }

  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
