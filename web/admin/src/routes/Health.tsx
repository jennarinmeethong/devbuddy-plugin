import { useParams } from "react-router-dom";
import { useMutation, useQuery } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { Alert, Badge, Button, Empty, Panel, Table, When } from "../components/ui";
import { Failure } from "../components/Failure";
import { t, tr } from "../i18n";
import { Hint, usePageTour } from "../components/Guide";
import { HINTS, TOURS } from "../guide/content";

/**
 * Component health.
 *
 * An authorised operation rather than the anonymous `/health` route, because it names components.
 * What each one reports is whether it answered, never a connection string or a credential: a
 * health page is one of the easiest places to leak one.
 */
export function Health() {
  const { workspaceId } = useParams();

  const health = useQuery({
    queryKey: ["health", workspaceId],
    queryFn: () => invoke("check_system_health", { workspaceId: workspaceId! }),
    enabled: Boolean(workspaceId),
  });

  usePageTour(TOURS.health);

  return (
    <>
      <Panel
        title={t("System health")}
        tour="health-components"
        actions={
          health.data ? (
            health.data.isHealthy ? (
              <Badge tone="live">{t("Healthy")}</Badge>
            ) : (
              <Badge>{t("Degraded")}</Badge>
            )
          ) : null
        }
      >
        {health.isPending ? (
          <Empty>{t("Checking…")}</Empty>
        ) : health.isError ? (
          <Failure error={health.error} />
        ) : (
          <Table head={[t("Component"), t("State"), t("Detail")]}>
            {health.data.components.map((component) => (
              <tr key={component.name} className="border-b border-[var(--color-line)] last:border-0">
                <td className="px-2 py-2">{component.name}</td>
                <td className="px-2 py-2">
                  {component.isHealthy ? <Badge tone="live">{t("Up")}</Badge> : <Badge>{t("Down")}</Badge>}
                </td>
                <td className="px-2 py-2 text-xs text-[var(--color-muted)]">{component.detail}</td>
              </tr>
            ))}
          </Table>
        )}
      </Panel>
      <Backup workspaceId={workspaceId!} />
    </>
  );
}

/**
 * A logical backup of the whole installation, written to the server's backup volume. Restoring
 * one is a console command, because a restore from total loss has nobody to authorise it.
 */
function Backup({ workspaceId }: { workspaceId: string }) {
  const backup = useMutation({ mutationFn: () => invoke("backup_system", { workspaceId }) });

  return (
    <Panel title={t("Backup")} tour="health-backup" hint={<Hint topic={HINTS.backup} />}>
      <div className="space-y-3">
        <p className="text-sm text-[var(--color-muted)]">
          {t("Writes a backup of every workspace to the server's backup volume. Copy it off that volume to keep it; restoring is done from the console.")}
        </p>
        <Button onClick={() => backup.mutate()} disabled={backup.isPending}>
          {t("Back up now")}
        </Button>
        {backup.isError ? <Failure error={backup.error} /> : null}
        {backup.data ? (
          <Alert tone="success">
            {tr("Backup {reference}, {size} bytes, at {when}.", {
              reference: <span className="font-mono">{backup.data.reference}</span>,
              size: backup.data.sizeBytes,
              when: <When value={backup.data.createdAt} />,
            })}
          </Alert>
        ) : null}
      </div>
    </Panel>
  );
}
