import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { Alert, Badge, Button, Empty, Field, Input, Panel, Table } from "../components/ui";
import { Failure } from "../components/Failure";
import { ProjectNav } from "../components/ProjectNav";
import { KIND_LABELS, KINDS, STATUS_LABELS, STATUSES, type RecordKind, type RecordStatus } from "../components/labels";

/**
 * Finding what the team already wrote down, the two ways the server offers.
 *
 * Full-text search is free and local and is where to start. Semantic search embeds the question
 * with the installation's model and, on a hosted provider, sends it out of the boundary, so it is
 * a separate, deliberate button rather than a mode of the first. When it cannot answer, the page
 * shows the server's reason instead of an empty list, because "not configured" and "no match" look
 * the same as nothing.
 */
export function Search() {
  const { workspaceId, projectId } = useParams();
  const scope = { workspaceId: workspaceId!, projectId: projectId! };
  const recordLink = (recordId: string) => `/w/${workspaceId}/p/${projectId}/records/${recordId}`;

  const [query, setQuery] = useState("");
  const [kinds, setKinds] = useState<RecordKind[]>([]);
  const [statuses, setStatuses] = useState<RecordStatus[]>(["Published"]);
  const [maxResults, setMaxResults] = useState(20);

  const fullText = useMutation({
    mutationFn: () =>
      invoke("search_knowledge", {
        scope,
        queryText: query,
        kinds: kinds.length > 0 ? kinds : null,
        statuses: statuses.length > 0 ? statuses : null,
        maxResults,
      }),
  });

  const semantic = useMutation({
    mutationFn: () => invoke("search_similar_records", { scope, queryText: query, maxResults: Math.min(maxResults, 50) }),
  });

  return (
    <>
      <ProjectNav title="Search" />

      <Panel title="Ask">
        <form
          className="space-y-3"
          onSubmit={(event) => {
            event.preventDefault();
            fullText.mutate();
          }}
        >
          <Field label="Question or words">
            <Input required value={query} onChange={(event) => setQuery(event.target.value)} />
          </Field>

          <Choices
            label="Kinds (full-text only)"
            hint="None ticked means every kind."
            options={KINDS}
            labels={KIND_LABELS}
            chosen={kinds}
            onChange={setKinds}
          />
          <Choices
            label="Statuses (full-text only)"
            hint="None ticked means every status, drafts included."
            options={STATUSES}
            labels={STATUS_LABELS}
            chosen={statuses}
            onChange={setStatuses}
          />

          <div className="w-40">
            <Field label="At most">
              <Input
                type="number"
                min={1}
                max={200}
                value={maxResults}
                onChange={(event) => setMaxResults(Number(event.target.value) || 1)}
              />
            </Field>
          </div>

          <div className="flex flex-wrap gap-2">
            <Button type="submit" variant="primary" disabled={fullText.isPending || query.trim() === ""}>
              Search the text
            </Button>
            <Button onClick={() => semantic.mutate()} disabled={semantic.isPending || query.trim() === ""}>
              Search by meaning
            </Button>
          </div>
        </form>
      </Panel>

      {fullText.isError ? <Failure error={fullText.error} /> : null}
      {fullText.data ? (
        <Panel title="Full-text results">
          {fullText.data.hits.length === 0 ? (
            <Empty>Nothing matched.</Empty>
          ) : (
            <Table head={["Title", "Kind", "Status", "Excerpt"]}>
              {fullText.data.hits.map((hit) => (
                <tr key={hit.recordId} className="border-b border-[var(--color-line)] last:border-0">
                  <td className="px-2 py-2">
                    <Link className="underline" to={recordLink(hit.recordId)}>
                      {hit.title}
                    </Link>
                  </td>
                  <td className="px-2 py-2 text-xs text-[var(--color-muted)]">{KIND_LABELS[hit.kind]}</td>
                  <td className="px-2 py-2">
                    {hit.status === "Published" ? (
                      <Badge tone="live">Published</Badge>
                    ) : (
                      <Badge>{STATUS_LABELS[hit.status]}</Badge>
                    )}
                  </td>
                  <td className="px-2 py-2 text-xs">{hit.snippet}</td>
                </tr>
              ))}
            </Table>
          )}
        </Panel>
      ) : null}

      {semantic.isError ? <Failure error={semantic.error} /> : null}
      {semantic.data ? (
        <Panel title="Results by meaning">
          {semantic.data.unavailable ? (
            <Alert>{semantic.data.unavailable}</Alert>
          ) : semantic.data.hits.length === 0 ? (
            <Empty>Nothing close enough.</Empty>
          ) : (
            <Table head={["Title", "Kind", "Revision", "Distance"]}>
              {semantic.data.hits.map((hit) => (
                <tr key={`${hit.recordId}-${hit.revisionNumber}`} className="border-b border-[var(--color-line)] last:border-0">
                  <td className="px-2 py-2">
                    <Link className="underline" to={recordLink(hit.recordId)}>
                      {hit.title}
                    </Link>
                  </td>
                  <td className="px-2 py-2 text-xs text-[var(--color-muted)]">{KIND_LABELS[hit.kind]}</td>
                  <td className="px-2 py-2 text-xs">{hit.revisionNumber}</td>
                  <td className="px-2 py-2 text-xs">{hit.distance.toFixed(3)}</td>
                </tr>
              ))}
            </Table>
          )}
        </Panel>
      ) : null}
    </>
  );
}

function Choices<T extends string>({
  label,
  hint,
  options,
  labels,
  chosen,
  onChange,
}: {
  label: string;
  hint: string;
  options: T[];
  labels: Record<T, string>;
  chosen: T[];
  onChange: (next: T[]) => void;
}) {
  return (
    <fieldset>
      <legend className="text-sm font-medium">{label}</legend>
      <div className="flex flex-wrap gap-3 py-1 text-sm">
        {options.map((option) => (
          <label key={option} className="flex items-center gap-1">
            <input
              type="checkbox"
              checked={chosen.includes(option)}
              onChange={(event) =>
                onChange(event.target.checked ? [...chosen, option] : chosen.filter((value) => value !== option))
              }
            />
            {labels[option]}
          </label>
        ))}
      </div>
      <p className="text-xs text-[var(--color-muted)]">{hint}</p>
    </fieldset>
  );
}
