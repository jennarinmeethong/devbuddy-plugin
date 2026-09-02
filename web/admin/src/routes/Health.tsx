import { useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { invoke } from "../api/client";
import { Badge, Empty, Panel, Table } from "../components/ui";
import { Failure } from "../components/Failure";

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

  return (
    <Panel
      title="System health"
      actions={
        health.data ? (
          health.data.isHealthy ? (
            <Badge tone="live">Healthy</Badge>
          ) : (
            <Badge>Degraded</Badge>
          )
        ) : null
      }
    >
      {health.isPending ? (
        <Empty>Checking…</Empty>
      ) : health.isError ? (
        <Failure error={health.error} />
      ) : (
        <Table head={["Component", "State", "Detail"]}>
          {health.data.components.map((component) => (
            <tr key={component.name} className="border-b border-[var(--color-line)] last:border-0">
              <td className="px-2 py-2">{component.name}</td>
              <td className="px-2 py-2">
                {component.isHealthy ? <Badge tone="live">Up</Badge> : <Badge>Down</Badge>}
              </td>
              <td className="px-2 py-2 text-xs text-[var(--color-muted)]">{component.detail}</td>
            </tr>
          ))}
        </Table>
      )}
    </Panel>
  );
}
