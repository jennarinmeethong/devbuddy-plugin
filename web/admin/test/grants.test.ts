import { expect, test } from "bun:test";
import type { WorkspaceAccess } from "../src/api/client";
import { mergeAccess } from "../src/api/session";

/**
 * Phase 13, A5: a person holding two grants in one workspace is offered what both allow, not what
 * the first one made happened to allow.
 */
const viewer: WorkspaceAccess = {
  workspaceId: "w",
  name: "Workspace",
  role: "Viewer",
  scopedToProject: null,
  permissions: ["ReadKnowledge", "ManageOwnCredentials"],
};

const reviewerOnAlpha: WorkspaceAccess = {
  workspaceId: "w",
  name: "Workspace",
  role: "Reviewer",
  scopedToProject: "alpha",
  permissions: ["ReadKnowledge", "CreateDraft", "ReviewRecord", "PublishRecord"],
};

test("inside a project, the workspace grant and that project's grant are merged, in either order", () => {
  for (const order of [[viewer, reviewerOnAlpha], [reviewerOnAlpha, viewer]]) {
    const access = mergeAccess(order, "alpha")!;
    expect(access.permissions).toContain("ReviewRecord");
    expect(access.permissions).toContain("ManageOwnCredentials");
  }
});

test("another project's grant is not counted inside a project it does not cover", () => {
  expect(mergeAccess([viewer, reviewerOnAlpha], "beta")!.permissions).not.toContain("ReviewRecord");
});

test("at workspace level only workspace-wide grants count, whatever order they were made in", () => {
  for (const order of [[viewer, reviewerOnAlpha], [reviewerOnAlpha, viewer]]) {
    const access = mergeAccess(order)!;
    expect(access.permissions).not.toContain("ReviewRecord");
    expect(access.scopedToProject).toBeNull();
  }
});

test("somebody with project grants only is still offered what those grants carry", () => {
  expect(mergeAccess([reviewerOnAlpha])!.permissions).toContain("ReviewRecord");
  expect(mergeAccess([])).toBeUndefined();
});
