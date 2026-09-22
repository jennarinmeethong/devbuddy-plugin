import { expect, test } from "bun:test";
import { QueryClient, QueryObserver } from "@tanstack/react-query";
import { refetchAfterWrite } from "../src/api/queries";

/**
 * A write made while a list is loading for the first time is shown once the list is re-read.
 *
 * The end-to-end suite found the case in CI: a project was created while the Projects screen's
 * first `list_projects` was still in flight, and the list never showed it. `invalidateQueries`
 * alone hands back a first fetch that is still running, and that fetch read the list before the
 * write.
 */
test("a write made during the first fetch is in the list afterwards", async () => {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const stored = ["existing"];
  let release: () => void = () => {};
  let calls = 0;

  const observer = new QueryObserver(queries, {
    queryKey: ["projects"],
    queryFn: async () => {
      calls += 1;
      const snapshot = [...stored];
      if (calls === 1) {
        // The first read has already taken its answer, and is slow to deliver it.
        await new Promise<void>((resolve) => {
          release = resolve;
        });
      }
      return snapshot;
    },
  });
  const unsubscribe = observer.subscribe(() => {});

  stored.push("created");
  const refreshed = refetchAfterWrite(queries, { queryKey: ["projects"] });
  release();
  await refreshed;

  expect(queries.getQueryData<string[]>(["projects"])).toEqual(["existing", "created"]);
  unsubscribe();
  queries.clear();
});
