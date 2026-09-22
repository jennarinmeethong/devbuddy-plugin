import type { InvalidateQueryFilters, QueryClient } from "@tanstack/react-query";

/**
 * Re-reads what a write changed. Every screen calls this after a write, not `invalidateQueries`
 * alone.
 *
 * `invalidateQueries` restarts a fetch in flight only when the query already holds data. A first
 * fetch has none, so the invalidation is handed that fetch back, and it began before the write.
 * Create a project while the list is still loading for the first time, and the list shows it
 * without the new project and never asks again. The end-to-end suite found it in CI. Cancelling
 * first leaves nothing in flight, so the invalidation always starts a fetch that follows the write.
 */
export async function refetchAfterWrite(queries: QueryClient, filters: InvalidateQueryFilters): Promise<void> {
  await queries.cancelQueries(filters);
  await queries.invalidateQueries(filters);
}
