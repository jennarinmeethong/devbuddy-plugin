import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { ProjectNav } from "../components/ProjectNav";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { grants, useWorkspace } from "../api/session";
import { refetchAfterWrite } from "../api/queries";
import { Button, Empty, Field, Input, Panel, Select, Table, TextArea, When } from "../components/ui";
import { Failure } from "../components/Failure";
import { m, t } from "../i18n";
import { Hint, usePageTour } from "../components/Guide";
import { HINTS, TOURS } from "../guide/content";

/**
 * Work items in a project: the anchor every knowledge record hangs off.
 *
 * `ChangeRequest` and `CodeReview` are spelled out here as they are everywhere else. They are
 * different record types, and the abbreviation that collapses them is banned from this codebase.
 */
const TYPES = ["Develop", "Enhance", "FixBug", "ChangeRequest", "CodeReview"] as const;

const TYPE_LABELS: Record<(typeof TYPES)[number], string> = {
  Develop: m("Develop"),
  Enhance: m("Enhance"),
  FixBug: m("Fix a bug"),
  ChangeRequest: m("Change request"),
  CodeReview: m("Code review"),
};

export function WorkItems() {
  const { workspaceId, projectId } = useParams();
  const access = useWorkspace(workspaceId);
  const scope = { workspaceId: workspaceId!, projectId: projectId! };

  usePageTour(TOURS.workItems);

  const items = useQuery({
    queryKey: ["work-items", workspaceId, projectId],
    queryFn: () => invoke("list_work_items", { scope }),
    enabled: Boolean(workspaceId && projectId),
  });

  return (
    <>
      <ProjectNav title={t("Work")} />

      <Panel title={t("Work items")} tour="work-items-list">
        {items.isPending ? (
          <Empty>{t("Loading…")}</Empty>
        ) : items.isError ? (
          <Failure error={items.error} />
        ) : items.data.workItems.length === 0 ? (
          <Empty>{t("Nothing registered yet.")}</Empty>
        ) : (
          <Table head={[t("Key"), t("Type"), t("Title"), t("Created")]}>
            {items.data.workItems.map((item) => (
              <tr key={item.workItemId} className="border-b border-[var(--color-line)] last:border-0">
                <td className="px-2 py-2 font-mono text-xs">{item.key}</td>
                <td className="px-2 py-2">{t(TYPE_LABELS[item.type])}</td>
                <td className="px-2 py-2">
                  <Link className="underline" to={`work/${item.workItemId}`}>
                    {item.title}
                  </Link>
                </td>
                <td className="px-2 py-2 text-xs">
                  <When value={item.createdAt} />
                </td>
              </tr>
            ))}
          </Table>
        )}
      </Panel>

      {grants(access, "ManageWorkItems") ? <NewWorkItem scope={scope} /> : null}
    </>
  );
}

function NewWorkItem({ scope }: { scope: { workspaceId: string; projectId: string } }) {
  const queries = useQueryClient();
  const [key, setKey] = useState("");
  const [type, setType] = useState<(typeof TYPES)[number]>("Develop");
  const [title, setTitle] = useState("");
  const [goal, setGoal] = useState("");
  const [inScope, setInScope] = useState("");
  const [exclusions, setExclusions] = useState("");

  const create = useMutation({
    mutationFn: () =>
      invoke("create_work_item", {
        scope,
        key,
        type,
        title,
        goal,
        inScope: inScope || null,
        exclusions: exclusions || null,
      }),
    onSuccess: async () => {
      setKey("");
      setTitle("");
      setGoal("");
      setInScope("");
      setExclusions("");
      await refetchAfterWrite(queries, { queryKey: ["work-items", scope.workspaceId, scope.projectId] });
    },
  });

  return (
    <Panel title={t("Register work")} tour="work-items-new">
      <form
        className="grid gap-3 sm:grid-cols-2"
        onSubmit={(event) => {
          event.preventDefault();
          create.mutate();
        }}
      >
        <Field label={t("Key")} hint={t("What the team says out loud, such as DEV-101.")}>
          <Input required value={key} onChange={(event) => setKey(event.target.value)} />
        </Field>

        <Field label={t("Type")} help={<Hint topic={HINTS.workType} />}>
          <Select value={type} onChange={(event) => setType(event.target.value as (typeof TYPES)[number])}>
            {TYPES.map((option) => (
              <option key={option} value={option}>
                {t(TYPE_LABELS[option])}
              </option>
            ))}
          </Select>
        </Field>

        <div className="sm:col-span-2">
          <Field label={t("Title")}>
            <Input required value={title} onChange={(event) => setTitle(event.target.value)} />
          </Field>
        </div>

        <div className="sm:col-span-2">
          <Field label={t("Goal")} hint={t("What this work is for. The part nobody writes down.")}>
            <TextArea required rows={2} value={goal} onChange={(event) => setGoal(event.target.value)} />
          </Field>
        </div>

        <Field label={t("In scope")}>
          <TextArea rows={2} value={inScope} onChange={(event) => setInScope(event.target.value)} />
        </Field>

        <Field
          label={t("Deliberately excluded")}
          hint={t("Usually what a later owner needs and never finds written down.")}
        >
          <TextArea rows={2} value={exclusions} onChange={(event) => setExclusions(event.target.value)} />
        </Field>

        <div className="sm:col-span-2">
          <Button type="submit" variant="primary" disabled={create.isPending}>
            {t("Register")}
          </Button>
        </div>
      </form>

      {create.isError ? (
        <div className="mt-3">
          <Failure error={create.error} />
        </div>
      ) : null}
    </Panel>
  );
}
