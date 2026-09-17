import { afterEach, beforeEach, describe, expect, test } from "bun:test";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactElement } from "react";
import { SessionProvider } from "../src/api/session";
import { App } from "../src/App";
import { forgetTokens } from "../src/api/client";
import {
  EVIDENCE,
  fakeServer,
  NEW_WORKSPACE,
  OTHER_USER,
  PROJECT,
  RECORD,
  recordIn,
  REPOSITORY,
  TEAM,
  USER,
  WORK_ITEM,
  WORKSPACE,
} from "./server";
import type { FakeServer, RecordStatus } from "./server";

/**
 * The Phase 8 smoke test: every capability info.md names is reachable and does what it says.
 *
 * "Reachable" is the point. These are not tests of the rules — a fake server cannot prove a rule —
 * they are proof that a person signed in can get to each screen, that each screen renders what the
 * server sent, and that the buttons call the operation they claim to. The rules are proved
 * server-side in DevBuddy.Api.Tests against real PostgreSQL, which is the only place they can be.
 */

let server: FakeServer;

function mount(path: string): ReactElement {
  const queries = new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  });

  return (
    <QueryClientProvider client={queries}>
      <MemoryRouter initialEntries={[path]}>
        <SessionProvider>
          <App />
        </SessionProvider>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

function signedIn(): void {
  sessionStorage.setItem("devbuddy.refresh", "refresh");
}

beforeEach(() => {
  sessionStorage.clear();
  server = fakeServer();
});

afterEach(() => {
  cleanup();
  forgetTokens();
  server.restore();
});

describe("signing in and recovering an account", () => {
  test("an unauthenticated visitor is offered sign-in and nothing else", async () => {
    render(mount("/"));

    expect(await screen.findByRole("button", { name: "Sign in" })).toBeDefined();
    expect(screen.queryByRole("navigation", { name: "Workspace" })).toBeNull();
  });

  test("signing in reaches the workspace", async () => {
    render(mount("/"));

    fireEvent.change(await screen.findByRole("textbox"), {
      target: { value: "administrator@example.test" },
    });

    fireEvent.change(document.querySelector("input[type=password]")!, {
      target: { value: "correct-horse-battery-staple" },
    });

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Sign in" }));
    });

    expect(await screen.findByRole("navigation", { name: "Workspace" })).toBeDefined();
  });

  test("account recovery is offered and never says whether an address exists", async () => {
    render(mount("/"));

    fireEvent.click(await screen.findByRole("button", { name: "Forgot your password?" }));

    expect(await screen.findByRole("button", { name: "Send a recovery token" })).toBeDefined();
  });

  test("a setup or recovery token can be redeemed", async () => {
    render(mount("/set-password"));

    expect(await screen.findByRole("button", { name: "Set password" })).toBeDefined();
  });
});

describe("workspace, project, and membership administration", () => {
  test("projects are listed with their AI access policy", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}`));

    expect(await screen.findByRole("link", { name: "Alpha" })).toBeDefined();

    // Denied is shown as state, not inferred from an unlit toggle.
    expect(await screen.findByText("Denied")).toBeDefined();
    expect(server.called("list_projects")).toBeDefined();
  });

  test("a project can be created", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}`));

    fireEvent.change(await screen.findByPlaceholderText("Payments platform"), {
      target: { value: "Gamma" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Create" }));

    await waitFor(() => expect(server.called("create_project")).toBeDefined());
  });

  test("AI access can be enabled for one project", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}`));

    fireEvent.click(await screen.findByRole("button", { name: "Enable AI access" }));

    await waitFor(() => expect(server.called("enable_project_ai_access")).toBeDefined());

    const call = server.called("enable_project_ai_access")!;
    expect(call.body).toEqual({ scope: { workspaceId: WORKSPACE, projectId: PROJECT } });
  });

  test("members are listed, including revoked grants", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/members`));

    expect(await screen.findByText("Reviewer")).toBeDefined();

    // Waited for rather than read at once: the role picker says "Reviewer" before the list has
    // been asked for, so an immediate check passes or fails on scheduling alone.
    await waitFor(() => expect(server.called("list_memberships")).toBeDefined());
  });

  test("somebody can be onboarded and their setup token is shown once", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/members`));

    const inputs = await screen.findAllByRole("textbox");

    fireEvent.change(inputs[0]!, { target: { value: "newcomer@example.test" } });
    fireEvent.change(inputs[1]!, { target: { value: "Newcomer" } });

    fireEvent.click(screen.getByRole("button", { name: "Create account" }));

    await waitFor(() => expect(server.called("create_user_account")).toBeDefined());
    expect(await screen.findByText("setup-token-for-the-newcomer")).toBeDefined();
  });

  test("a membership can be revoked", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/members`));

    const revokes = await screen.findAllByRole("button", { name: "Revoke" });
    fireEvent.click(revokes[0]!);

    await waitFor(() => expect(server.called("revoke_membership")).toBeDefined());
  });

  test("a project is deleted only after its name is typed back", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}`));

    fireEvent.click(await screen.findByRole("button", { name: "Delete" }));

    const confirm = screen.getByRole("button", { name: "Delete for good" });

    // Armed but inert: a confirmation somebody can click through is not a confirmation, and this
    // one takes records, their history, and the evidence behind them with no undo.
    expect(confirm.hasAttribute("disabled")).toBe(true);

    fireEvent.change(screen.getByLabelText("Type Alpha to confirm deletion"), {
      target: { value: "Alpha" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Delete for good" }));

    await waitFor(() => expect(server.called("delete_project")).toBeDefined());

    expect(server.called("delete_project")!.body).toEqual({
      scope: { workspaceId: WORKSPACE, projectId: PROJECT },
    });
  });
});

describe("team administration", () => {
  test("teams are listed and one can be created", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/teams`));

    expect(await screen.findByRole("button", { name: "Platform" })).toBeDefined();
    expect(server.called("list_teams")).toBeDefined();

    fireEvent.change(screen.getByPlaceholderText("Platform"), { target: { value: "Payments" } });
    fireEvent.click(screen.getByRole("button", { name: "Create" }));

    await waitFor(() => expect(server.called("create_team")).toBeDefined());
    expect(server.called("create_team")!.body).toEqual({ workspaceId: WORKSPACE, name: "Payments" });
  });

  test("a team can be renamed", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/teams`));

    fireEvent.click(await screen.findByRole("button", { name: "Rename" }));

    fireEvent.change(screen.getByLabelText("New name for Platform"), {
      target: { value: "Platform Engineering" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(server.called("rename_team")).toBeDefined());

    expect(server.called("rename_team")!.body).toEqual({
      workspaceId: WORKSPACE,
      teamId: TEAM,
      name: "Platform Engineering",
    });
  });

  test("a team is deleted only after confirming", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/teams`));

    fireEvent.click(await screen.findByRole("button", { name: "Delete" }));
    expect(server.called("delete_team")).toBeUndefined();

    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(server.called("delete_team")).toBeDefined());
  });

  test("somebody is added to a team by picking them, never by typing an identifier", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/teams`));

    fireEvent.click(await screen.findByRole("button", { name: "Members" }));

    // Already on the team, so listed as a member rather than offered as a candidate.
    expect(await screen.findByText(USER)).toBeDefined();

    const picker = await screen.findByRole("combobox", { name: "Add somebody to Platform" });
    fireEvent.change(picker, { target: { value: OTHER_USER } });
    fireEvent.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => expect(server.called("add_team_member")).toBeDefined());

    expect(server.called("add_team_member")!.body).toEqual({
      workspaceId: WORKSPACE,
      teamId: TEAM,
      userId: OTHER_USER,
    });
  });

  test("somebody can be removed from a team", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/teams`));

    fireEvent.click(await screen.findByRole("button", { name: "Members" }));
    fireEvent.click(await screen.findByRole("button", { name: "Remove" }));

    await waitFor(() => expect(server.called("remove_team_member")).toBeDefined());
  });
});

describe("standing up another workspace", () => {
  test("the workspaces you can reach are listed and a new one can be created", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/workspaces`));

    expect(await screen.findByRole("link", { name: "Acme" })).toBeDefined();

    fireEvent.change(screen.getByPlaceholderText("Northwind"), { target: { value: "Northwind" } });
    fireEvent.click(screen.getByRole("button", { name: "Create workspace" }));

    await waitFor(() => expect(server.called("create_workspace")).toBeDefined());

    // Sponsored by the workspace being viewed, which is what the permission is held on. An
    // installation-wide role would not need to name one, and this system has none.
    expect(server.called("create_workspace")!.body).toEqual({
      sponsorWorkspaceId: WORKSPACE,
      name: "Northwind",
      firstProjectName: null,
    });
  });

  test("the confirmation stays on screen while the session is re-read behind it", async () => {
    // Creating a workspace re-reads /me. Until 2026-09-17 that re-read put App back on its
    // "Loading…" screen, which unmounted this one and took the confirmation with it. The re-read
    // is held back a little, as a network would, or React folds "loading" and "loaded" into one
    // render and the defect never shows.
    const answer = globalThis.fetch;
    let reads = 0;
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input) === "/me") {
        reads += 1;

        if (reads > 1) {
          await new Promise((resolve) => setTimeout(resolve, 50));
        }
      }

      return answer(input, init);
    }) as typeof fetch;

    signedIn();
    render(mount(`/w/${WORKSPACE}/workspaces`));

    fireEvent.change(await screen.findByPlaceholderText("Northwind"), {
      target: { value: "Northwind" },
    });

    const before = reads;
    fireEvent.click(screen.getByRole("button", { name: "Create workspace" }));

    await waitFor(() => expect(reads).toBeGreaterThan(before));

    const link = await screen.findByRole("link", { name: "open Northwind" });
    expect(link.getAttribute("href")).toBe(`/w/${NEW_WORKSPACE}`);
    expect(screen.getByText(/Created, sponsored by Acme/)).toBeDefined();
    expect(screen.queryByText("Loading…")).toBeNull();
  });

  test("a first project comes along when one is named", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/workspaces`));

    fireEvent.change(await screen.findByPlaceholderText("Northwind"), {
      target: { value: "Northwind" },
    });

    fireEvent.change(screen.getByPlaceholderText("Payments platform"), {
      target: { value: "Payments" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Create workspace" }));

    await waitFor(() => expect(server.called("create_workspace")).toBeDefined());
    expect(server.called("create_workspace")!.body).toMatchObject({ firstProjectName: "Payments" });
  });
});

describe("work and knowledge", () => {
  test("work items are listed and can be registered", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}`));

    const row = (await screen.findByText("CRQ-101")).closest("tr")!;

    // Spelled out, never abbreviated. Change request and code review are different record types,
    // and the two-letter contraction that collapses them is banned from this codebase.
    expect(within(row).getByText("Change request")).toBeDefined();

    // Every required field, because the form is the same one a person fills in and a browser
    // will not submit it half-empty either.
    const fields = screen.getAllByRole("textbox");
    fireEvent.change(fields[0]!, { target: { value: "DEV-9" } });
    fireEvent.change(fields[1]!, { target: { value: "Record what this work is" } });
    fireEvent.change(fields[2]!, { target: { value: "So the next person does not have to guess." } });

    fireEvent.click(screen.getByRole("button", { name: "Register" }));
    await waitFor(() => expect(server.called("create_work_item")).toBeDefined());
  });

  test("the review queue defaults to what is waiting for approval", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}/records`));

    await waitFor(() => expect(server.called("list_records")).toBeDefined());

    expect(server.called("list_records")!.body).toEqual({
      scope: { workspaceId: WORKSPACE, projectId: PROJECT },
      statuses: ["PendingApproval"],
    });

    expect(await screen.findByRole("link", { name: "Rollback is a migration, not a restore" })).toBeDefined();
  });

  test("published record history shows every revision and its hash", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}/records/${RECORD}`));

    const history = await screen.findByRole("heading", { name: "History" });
    const panel = history.closest("section")!;

    // The heading is drawn while the history is still loading, so wait for what it lists.
    expect(await within(panel).findByText(/Revision 2/)).toBeDefined();
    expect(within(panel).getByText("a".repeat(64))).toBeDefined();
  });

  test("a record's body is read by revision number and labelled with whether it is published", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}/records/${RECORD}`));

    expect(await screen.findByText("Rolling back a migration is itself a migration.")).toBeDefined();

    // Without a number get_record serves the published revision only, and this record has none.
    // The screen asks for the revision it offers for approval instead of relying on a default
    // (SB-26).
    expect(server.called("get_record")!.body).toEqual({
      scope: { workspaceId: WORKSPACE, projectId: PROJECT },
      recordId: RECORD,
      revisionNumber: 2,
    });

    expect(screen.getByText(/never published/)).toBeDefined();
  });
});

describe("the approval screen", () => {
  test("it names the revision being approved and submits that revision's hash", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}/records/${RECORD}`));

    fireEvent.click(await screen.findByRole("button", { name: "Approve revision 2" }));

    await waitFor(() => expect(server.called("approve_record")).toBeDefined());

    // The hash on screen, not a request for whatever is newest. This is what makes the UI unable
    // to approve "the latest" implicitly (SB-23).
    expect(server.called("approve_record")!.body).toEqual({
      scope: { workspaceId: WORKSPACE, projectId: PROJECT },
      recordId: RECORD,
      approvedContentHash: "a".repeat(64),
    });
  });

  test("a reviewer can send a record back instead", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}/records/${RECORD}`));

    fireEvent.change(await screen.findByRole("textbox"), {
      target: { value: "The rollback section is wrong." },
    });

    fireEvent.click(screen.getByRole("button", { name: "Request a correction" }));

    await waitFor(() => expect(server.called("request_correction")).toBeDefined());
  });
});

describe("the record lifecycle", () => {
  // What RolePermissions gives each role. Written out rather than imported, because the server is
  // the authority and this file only needs to look like what /me would report.
  const VIEWER = ["ReadKnowledge", "ManageOwnCredentials"];
  const CONTRIBUTOR = [...VIEWER, "AnalyzeProject", "CreateDraft", "ManageWorkItems"];

  const recordPage = `/w/${WORKSPACE}/p/${PROJECT}/records/${RECORD}`;

  function serve(permissions: string[] | undefined, status: RecordStatus): void {
    server.restore();
    server = fakeServer(permissions, recordIn(status));
  }

  test("a draft can be submitted for approval by somebody who may draft", async () => {
    serve(CONTRIBUTOR, "Draft");

    signedIn();
    render(mount(recordPage));

    fireEvent.click(await screen.findByRole("button", { name: "Submit for approval" }));

    await waitFor(() => expect(server.called("submit_for_approval")).toBeDefined());

    expect(server.called("submit_for_approval")!.body).toEqual({
      scope: { workspaceId: WORKSPACE, projectId: PROJECT },
      recordId: RECORD,
    });

    // The page reads the record again rather than assuming what the server did with it.
    await waitFor(() =>
      expect(server.calls.filter((call) => call.operation === "view_record_history").length).toBe(2),
    );
  });

  test("a viewer opening a draft is not offered submission", async () => {
    serve(VIEWER, "Draft");

    signedIn();
    render(mount(recordPage));

    expect(await screen.findByText("Draft")).toBeDefined();
    expect(screen.queryByRole("button", { name: "Submit for approval" })).toBeNull();
  });

  // Each state names something that must be on the page, so the absence of submission is read
  // after the page has decided what to offer rather than before it has loaded.
  const offeredInstead: [RecordStatus, string | null][] = [
    ["PendingApproval", "Approve revision 2"],
    ["Approved", "Publish"],
    ["Published", "Archive"],
    ["Archived", null],
  ];

  for (const [status, present] of offeredInstead) {
    test(`a record that is ${status} is not offered submission`, async () => {
      serve(undefined, status);

      signedIn();
      render(mount(recordPage));

      if (present) {
        expect(await screen.findByRole("button", { name: present })).toBeDefined();
      } else {
        expect(await screen.findByText(status)).toBeDefined();
      }

      expect(screen.queryByRole("button", { name: "Submit for approval" })).toBeNull();
    });
  }

  test("a record is archived only after confirming", async () => {
    serve(undefined, "Published");

    signedIn();
    render(mount(recordPage));

    fireEvent.click(await screen.findByRole("button", { name: "Archive" }));
    expect(server.called("archive_record")).toBeUndefined();

    fireEvent.click(screen.getByRole("button", { name: "Archive for good" }));

    await waitFor(() => expect(server.called("archive_record")).toBeDefined());

    expect(server.called("archive_record")!.body).toEqual({
      scope: { workspaceId: WORKSPACE, projectId: PROJECT },
      recordId: RECORD,
    });
  });

  test("archiving is not offered to a contributor", async () => {
    serve(CONTRIBUTOR, "Draft");

    signedIn();
    render(mount(recordPage));

    expect(await screen.findByRole("button", { name: "Submit for approval" })).toBeDefined();
    expect(screen.queryByRole("button", { name: "Archive" })).toBeNull();
  });

  test("nothing is offered on a record that is already archived", async () => {
    serve(undefined, "Archived");

    signedIn();
    render(mount(recordPage));

    expect(await screen.findByText("Archived")).toBeDefined();

    for (const name of ["Archive", "Submit for approval", "Approve revision 2", "Publish"]) {
      expect(screen.queryByRole("button", { name })).toBeNull();
    }
  });
});

describe("approving a revision of a published record", () => {
  const PUBLISHED_HASH = "b".repeat(64);
  const PENDING_HASH = "a".repeat(64);

  const provenance = {
    sourceKind: "HumanAuthored",
    sourceLocator: "meeting/2026-09-01",
    author: "A person",
    recordedAt: "2026-09-01T09:00:00+00:00",
    isAiGenerated: false,
    evidenceCount: 0,
  };

  function revision(number: number, body: string) {
    return {
      recordId: RECORD,
      kind: "Decision",
      status: "PendingApproval",
      revisionNumber: number,
      publishedRevisionNumber: 1,
      title: "Rollback is a migration, not a restore",
      body,
      provenance,
      lastUpdatedAt: "2026-09-02T10:00:00+00:00",
      frontMatter: {},
      evidence: [],
    };
  }

  test("the reviewer is shown the revision the approval binds, not the published one (SB-23)", async () => {
    server.restore();
    server = fakeServer(undefined, {
      // As the server does: no revision named means the published one.
      get_record: (body: { revisionNumber?: number | null }) =>
        body.revisionNumber === 2
          ? revision(2, "Roll forward with a compensating migration.")
          : revision(1, "Restore last night's backup."),
      view_record_history: {
        recordId: RECORD,
        status: "PendingApproval",
        revisions: [
          {
            number: 1,
            contentHash: PUBLISHED_HASH,
            title: "Rollback is a migration, not a restore",
            createdAt: "2026-09-01T10:00:00+00:00",
            provenance,
            isPublished: true,
            approval: null,
          },
          {
            number: 2,
            contentHash: PENDING_HASH,
            title: "Rollback is a migration, not a restore",
            createdAt: "2026-09-02T10:00:00+00:00",
            provenance,
            isPublished: false,
            approval: null,
          },
        ],
        corrections: [],
      },
    });

    signedIn();
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}/records/${RECORD}`));

    const pending = (await screen.findByRole("heading", { name: "Revision 2 — not published" })).closest("section")!;
    expect(await within(pending).findByText("Roll forward with a compensating migration.")).toBeDefined();

    // Still shown, and labelled as what readers see rather than as what is being approved.
    const published = screen.getByRole("heading", { name: "Published content" }).closest("section")!;
    expect(within(published).getByText("Restore last night's backup.")).toBeDefined();

    fireEvent.click(screen.getByRole("button", { name: "Approve revision 2" }));

    await waitFor(() => expect(server.called("approve_record")).toBeDefined());
    expect(server.called("approve_record")!.body).toMatchObject({ approvedContentHash: PENDING_HASH });
  });
});

describe("audit and health", () => {
  test("the audit trail is readable per project", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/audit`));

    // The list of projects arrives after the picker is drawn, and choosing a value that has no
    // option yet selects nothing.
    await screen.findByRole("option", { name: "Alpha" });

    fireEvent.change(screen.getByRole("combobox", { name: "Project" }), {
      target: { value: PROJECT },
    });

    await waitFor(() => expect(server.called("read_audit_history")).toBeDefined());
    expect(await screen.findByText("RecordApproved")).toBeDefined();

    // The channel is shown beside the actor, because the actor alone cannot say whether an
    // assistant or the person holding the token made the call.
    expect(await screen.findByText("Assistant")).toBeDefined();

    fireEvent.change(screen.getByRole("combobox", { name: "Channel" }), { target: { value: "Ai" } });

    await waitFor(() =>
      expect(
        server.calls.some(
          (call) =>
            call.operation === "read_audit_history" &&
            (call.body as { channel?: string } | null)?.channel === "Ai",
        ),
      ).toBe(true),
    );
  });

  test("component health is shown", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/health`));

    expect(await screen.findByText("database")).toBeDefined();
    expect(server.called("check_system_health")).toBeDefined();
  });
});

describe("plugin access", () => {
  test("a token can be minted and is shown exactly once", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/plugin-access`));

    expect(await screen.findByText("Work laptop")).toBeDefined();

    fireEvent.change(await screen.findByPlaceholderText("Work laptop"), {
      target: { value: "Second laptop" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Mint" }));

    await waitFor(() => expect(server.called("issue_machine_token")).toBeDefined());
    expect(await screen.findByText("the-token-shown-exactly-once")).toBeDefined();

    // Minted for the workspace in the route, and for no other. Nothing on the page asks which
    // one, because there is nothing to ask: it is where you already are.
    expect(
      (server.called("issue_machine_token")?.body as { workspaceId: string }).workspaceId,
    ).toBe(WORKSPACE);
  });

  test("the page says the token is limited to this workspace", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/plugin-access`));

    expect(
      await screen.findByText(/in this workspace and no other/i),
    ).toBeDefined();

    // And that the assistant holding it is not what the token identifies.
    expect(await screen.findByText(/works in Claude and in Codex/i)).toBeDefined();
  });

  test("a token from before workspace scoping is shown as needing replacement", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/plugin-access`));

    expect(await screen.findByText("Old desktop")).toBeDefined();
    expect(await screen.findByText("Needs replacing")).toBeDefined();

    // It cannot be repaired — only a hash was ever stored — so the page has to say what to do
    // instead, rather than leaving somebody waiting for it to start working again.
    expect(await screen.findByText(/no longer works/i)).toBeDefined();
  });

  test("a token can be revoked", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/plugin-access`));

    // One per row: the live token, and the leftover that needs replacing. A row nobody can tidy
    // away is a row that stays on the page for ever.
    const buttons = await screen.findAllByRole("button", { name: "Revoke" });
    expect(buttons.length).toBe(2);

    fireEvent.click(buttons[0]!);

    await waitFor(() => expect(server.called("revoke_machine_token")).toBeDefined());
  });
});

describe("role-driven navigation", () => {
  test("a viewer is offered neither membership administration nor the audit trail", async () => {
    server.restore();
    server = fakeServer(["ReadKnowledge", "ManageOwnCredentials"]);

    signedIn();
    render(mount(`/w/${WORKSPACE}`));

    const nav = await screen.findByRole("navigation", { name: "Workspace" });

    expect(within(nav).getByRole("link", { name: "Projects" })).toBeDefined();
    expect(within(nav).queryByRole("link", { name: "Members" })).toBeNull();
    expect(within(nav).queryByRole("link", { name: "Audit" })).toBeNull();
    expect(within(nav).queryByRole("link", { name: "Health" })).toBeNull();

    // Nor team administration, nor standing up a workspace of their own.
    expect(within(nav).queryByRole("link", { name: "Teams" })).toBeNull();
    expect(within(nav).queryByRole("link", { name: "Workspaces" })).toBeNull();

    // Plugin access is offered, and to a viewer as much as to anybody. A token carries its
    // owner's permissions and no more, so being able to mint one grants nothing.
    expect(within(nav).getByRole("link", { name: "Plugin access" })).toBeDefined();

    // And no create form, because creating a project is not something a viewer may do. The server
    // refuses it either way; that is proved in DevBuddy.Api.Tests, not here.
    expect(screen.queryByRole("button", { name: "Create" })).toBeNull();
  });

  test("a viewer opening the approval screen is not offered approval", async () => {
    server.restore();
    server = fakeServer(["ReadKnowledge"]);

    signedIn();
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}/records/${RECORD}`));

    await screen.findByRole("heading", { name: "History" });

    expect(screen.queryByRole("button", { name: "Approve revision 2" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Request a correction" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Publish" })).toBeNull();
  });
});

describe("evidence", () => {
  test("what a project holds is listed, and a contributor can attach a file", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}/evidence`));

    await screen.findByRole("heading", { name: "Attached to this project" });

    // What the server sent, rendered. Waited for: the panel heading is drawn before the list
    // arrives.
    expect(await screen.findByText("text/plain")).toBeDefined();
    expect(screen.getByText("2 KB")).toBeDefined();

    const file = new File(["a build log"], "build.log", { type: "text/plain" });
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;

    Object.defineProperty(input, "files", { value: [file], configurable: true });
    fireEvent.change(input);

    fireEvent.change(screen.getByPlaceholderText("The build log for release 1.4"), {
      target: { value: "The build log" },
    });

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Attach" }));
    });

    // Posted to the evidence route rather than through an operation, carrying both parts.
    await waitFor(() => expect(server.uploads.length).toBe(1));
    expect(server.uploads[0].fileName).toBe("build.log");
    expect(server.uploads[0].description).toBe("The build log");
  });

  test("a viewer sees what is attached and is not offered the form", async () => {
    server.restore();
    server = fakeServer(["ReadKnowledge"]);

    signedIn();
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}/evidence`));

    await screen.findByRole("heading", { name: "Attached to this project" });

    // Reading is offered; attaching is not. The server refuses it either way, which is proved in
    // DevBuddy.Security.Tests against real PostgreSQL rather than here.
    expect((await screen.findAllByRole("button", { name: "Download" })).length).toBe(2);
    expect(screen.queryByRole("heading", { name: "Attach an artefact" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Attach" })).toBeNull();
  });

  test("an artefact that has not been cleared cannot be downloaded", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}/evidence`));

    await screen.findByRole("heading", { name: "Attached to this project" });

    // Two rows: one cleared, one still NotScanned. The server refuses to release the second, and
    // the screen must not invite a click that is going to be refused.
    const buttons = await screen.findAllByRole("button", { name: "Download" });

    expect(buttons.length).toBe(2);
    expect(buttons[0].hasAttribute("disabled")).toBe(false);
    expect(buttons[1].hasAttribute("disabled")).toBe(true);

    expect(screen.getByText("Clean")).toBeDefined();
    expect(screen.getByText("NotScanned")).toBeDefined();
  });
});

describe("editing a draft", () => {
  const VIEWER = ["ReadKnowledge", "ManageOwnCredentials"];
  const CONTRIBUTOR = [...VIEWER, "AnalyzeProject", "CreateDraft", "ManageWorkItems"];
  const recordPage = `/w/${WORKSPACE}/p/${PROJECT}/records/${RECORD}`;
  const REASON = "Say what happens to a failed batch.";

  function serveDraft(permissions: string[] | undefined, status: RecordStatus = "Draft"): void {
    const base = recordIn(status);

    server.restore();
    server = fakeServer(permissions, {
      get_record: {
        ...(base.get_record as object),
        frontMatter: { owner: "Platform team", area: "import" },
        evidence: [{ evidenceObjectId: EVIDENCE, description: "Import run log" }],
      },
      view_record_history: {
        ...(base.view_record_history as object),
        corrections: [
          {
            requestedBy: OTHER_USER,
            targetRevisionNumber: 2,
            reason: REASON,
            requestedAt: "2026-09-02T12:00:00+00:00",
          },
        ],
      },
      revise_draft: { recordId: RECORD, status: "Draft", currentRevisionNumber: 3, publishedRevisionNumber: null },
    });
  }

  test("the newest revision is edited and its front matter and evidence are sent back with it", async () => {
    serveDraft(CONTRIBUTOR);

    signedIn();
    render(mount(recordPage));

    fireEvent.click(await screen.findByRole("button", { name: "Edit this draft" }));

    fireEvent.change(await screen.findByRole("textbox", { name: "Body" }), {
      target: { value: "Failed batches are retried five times." },
    });
    fireEvent.change(screen.getByRole("textbox", { name: "Field 1 value" }), {
      target: { value: "Import team" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Save as a new revision" }));

    await waitFor(() => expect(server.called("revise_draft")).toBeDefined());

    expect(server.called("revise_draft")!.body).toMatchObject({
      scope: { workspaceId: WORKSPACE, projectId: PROJECT },
      recordId: RECORD,
      title: "Rollback is a migration, not a restore",
      body: "Failed batches are retried five times.",
      frontMatter: { owner: "Import team", area: "import" },
      provenance: {
        sourceKind: "HumanAuthored",
        sourceLocator: "meeting/2026-09-01",
        author: "An Administrator",
        evidence: [{ evidenceObjectId: EVIDENCE, description: "Import run log" }],
      },
    });

    // Nothing the editor read leaned on get_record's default, which serves only a published
    // revision (SB-26).
    const reads = server.calls.filter((call) => call.operation === "get_record");
    expect(reads.length).toBeGreaterThan(0);
    for (const read of reads) {
      expect((read.body as { revisionNumber?: number }).revisionNumber).toBe(2);
    }
  });

  test("a front matter field can be removed and another added", async () => {
    serveDraft(CONTRIBUTOR);

    signedIn();
    render(mount(recordPage));

    fireEvent.click(await screen.findByRole("button", { name: "Edit this draft" }));
    fireEvent.click(await screen.findByRole("button", { name: "Remove field 2" }));
    fireEvent.click(screen.getByRole("button", { name: "Add a field" }));

    fireEvent.change(screen.getByRole("textbox", { name: "Field 2 name" }), { target: { value: "reviewed-by" } });
    fireEvent.change(screen.getByRole("textbox", { name: "Field 2 value" }), { target: { value: "Jennarin" } });

    fireEvent.click(screen.getByRole("button", { name: "Save as a new revision" }));

    await waitFor(() => expect(server.called("revise_draft")).toBeDefined());

    expect((server.called("revise_draft")!.body as { frontMatter: unknown }).frontMatter).toEqual({
      owner: "Platform team",
      "reviewed-by": "Jennarin",
    });
  });

  test("a field named twice cannot be saved", async () => {
    serveDraft(CONTRIBUTOR);

    signedIn();
    render(mount(recordPage));

    fireEvent.click(await screen.findByRole("button", { name: "Edit this draft" }));
    fireEvent.change(await screen.findByRole("textbox", { name: "Field 2 name" }), { target: { value: "owner" } });

    expect(screen.getByText(/is named twice/)).toBeDefined();
    expect((screen.getByRole("button", { name: "Save as a new revision" }) as HTMLButtonElement).disabled).toBe(true);
  });

  test("a viewer opening a draft is not offered the editor, but reads why it was sent back", async () => {
    serveDraft(VIEWER);

    signedIn();
    render(mount(recordPage));

    expect((await screen.findAllByText(REASON)).length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: "Edit this draft" })).toBeNull();
  });

  const offeredInstead: [RecordStatus, string | null][] = [
    ["PendingApproval", "Approve revision 2"],
    ["Approved", "Publish"],
    ["Published", "Archive"],
    ["Archived", null],
  ];

  for (const [status, present] of offeredInstead) {
    test(`a record that is ${status} is not offered the editor`, async () => {
      serveDraft(undefined, status);

      signedIn();
      render(mount(recordPage));

      if (present) {
        expect(await screen.findByRole("button", { name: present })).toBeDefined();
      } else {
        expect(await screen.findByText(status)).toBeDefined();
      }

      expect(screen.queryByRole("button", { name: "Edit this draft" })).toBeNull();
    });
  }

  test("the person revising is told why the draft came back", async () => {
    serveDraft(CONTRIBUTOR);

    signedIn();
    render(mount(recordPage));

    const editor = (await screen.findByRole("heading", { name: "Revise this draft" })).closest("section")!;
    expect(await within(editor).findByText(new RegExp(REASON))).toBeDefined();
  });

  test("a reviewer is shown the front matter and evidence of what they are approving", async () => {
    serveDraft(undefined, "PendingApproval");

    signedIn();
    render(mount(recordPage));

    const matter = (await screen.findByText("owner")).closest("dl")!;
    expect(within(matter).getByText("Platform team")).toBeDefined();
    expect(within(matter).getByText("import")).toBeDefined();
    expect(screen.getByText(/Import run log/)).toBeDefined();
  });
});

/**
 * Every operation a person needs is reachable from a screen (info.md, 2026-09-17). Each test below
 * proves one screen calls the operation it claims to, with the arguments it showed. The rules are
 * the server's, and are proved there.
 */
describe("every operation has a screen", () => {
  const scope = { workspaceId: WORKSPACE, projectId: PROJECT };
  const project = `/w/${WORKSPACE}/p/${PROJECT}`;

  function textbox(name: RegExp): HTMLElement {
    return screen.getByRole("textbox", { name });
  }

  test("a work item opens from the list", async () => {
    signedIn();
    render(mount(project));

    const link = await screen.findByRole("link", { name: "Normalise identifiers on import" });
    expect(link.getAttribute("href")).toBe(`${project}/work/${WORK_ITEM}`);
  });

  test("a person writes a draft from its work item, and says where it came from", async () => {
    signedIn();
    render(mount(`${project}/work/${WORK_ITEM}`));

    expect(await screen.findByText("Identifiers are compared the same way everywhere.")).toBeDefined();

    fireEvent.change(textbox(/^Source/), { target: { value: "meeting/2026-09-02" } });
    fireEvent.change(textbox(/^Title/), { target: { value: "Identifiers are normalised first" } });
    fireEvent.change(textbox(/^Body/), { target: { value: "Because the validator compares them." } });

    fireEvent.click(screen.getByRole("button", { name: "Save draft" }));
    await waitFor(() => expect(server.called("create_draft")).toBeDefined());

    const body = server.called("create_draft")!.body as {
      provenance: Record<string, unknown>;
      [key: string]: unknown;
    };

    expect(body).toMatchObject({
      scope,
      workItemId: WORK_ITEM,
      kind: "Decision",
      title: "Identifiers are normalised first",
      body: "Because the validator compares them.",
      frontMatter: {},
      provenance: { sourceKind: "HumanAuthored", sourceLocator: "meeting/2026-09-02", evidence: [] },
    });

    // Whether an AI wrote it is the server's to decide from the channel, never the page's to say.
    expect("isAiGenerated" in body.provenance).toBe(false);
  });

  test("a viewer is not offered the draft form", async () => {
    server.restore();
    server = fakeServer(["ReadKnowledge"]);

    signedIn();
    render(mount(`${project}/work/${WORK_ITEM}`));

    expect(await screen.findByRole("heading", { name: "Handing this work over" })).toBeDefined();
    expect(screen.queryByRole("heading", { name: "Write a new draft" })).toBeNull();
  });

  test("a handover, open questions and missing evidence are one click each", async () => {
    signedIn();
    render(mount(`${project}/work/${WORK_ITEM}`));

    fireEvent.click(await screen.findByRole("button", { name: "Generate a handover" }));
    expect(await screen.findByText("Identifiers are normalised before validation.")).toBeDefined();
    expect(server.called("generate_handover")!.body).toEqual({ scope, workItemId: WORK_ITEM });

    fireEvent.click(screen.getByRole("button", { name: "Find missing evidence" }));
    expect(await screen.findByText("The decision cites no test run.")).toBeDefined();

    fireEvent.click(screen.getByRole("button", { name: "Find open questions" }));
    await waitFor(() => expect(server.called("find_open_questions")).toBeDefined());
    expect(server.called("find_open_questions")!.body).toEqual({ scope, workItemId: WORK_ITEM });
  });

  test("search finds by text, and semantic search shows the server's reason when it cannot answer", async () => {
    signedIn();
    render(mount(`${project}/search`));

    fireEvent.change(await screen.findByRole("textbox", { name: /^Question or words/ }), {
      target: { value: "rollback" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Search the text" }));
    expect(await screen.findByText("Rolling back a migration is itself a migration.")).toBeDefined();

    // Published only by default: a draft is not knowledge yet.
    expect(server.called("search_knowledge")!.body).toEqual({
      scope,
      queryText: "rollback",
      kinds: null,
      statuses: ["Published"],
      maxResults: 20,
    });

    fireEvent.click(screen.getByRole("button", { name: "Search by meaning" }));
    expect(await screen.findByText(/no embedding provider configured/)).toBeDefined();
    expect(server.called("search_similar_records")!.body).toEqual({ scope, queryText: "rollback", maxResults: 20 });
  });

  test("analysis offers the project's repositories and runs what was chosen", async () => {
    signedIn();
    render(mount(`${project}/analysis`));

    expect(await screen.findByText(REPOSITORY)).toBeDefined();
    expect(server.called("list_source_repositories")!.body).toEqual({ scope });

    const run = screen.getByRole("heading", { name: "Run an analysis" }).closest("section")!;
    const [what, repository] = within(run).getAllByRole("combobox");
    fireEvent.change(what!, { target: { value: "analyze_code" } });
    fireEvent.change(repository!, { target: { value: REPOSITORY } });
    fireEvent.change(within(run).getByRole("textbox"), { target: { value: "src" } });
    fireEvent.click(within(run).getByRole("button", { name: "Analyse" }));

    expect(await screen.findByText("Twelve source files, two projects.")).toBeDefined();
    expect(server.called("analyze_code")!.body).toEqual({ scope, repositoryId: REPOSITORY, target: "src" });
  });

  test("the whole project can be analysed without naming a repository", async () => {
    signedIn();
    render(mount(`${project}/analysis`));

    await screen.findByText(REPOSITORY);
    fireEvent.click(screen.getByRole("button", { name: "Analyse" }));

    await waitFor(() => expect(server.called("analyze_project")).toBeDefined());
    expect(server.called("analyze_project")!.body).toEqual({ scope, repositoryId: null, target: null });
  });

  test("change impact, comparing references and synchronising each call their operation", async () => {
    signedIn();
    render(mount(`${project}/analysis`));

    const impact = (await screen.findByRole("heading", { name: "What a change affects" })).closest("section")!;
    fireEvent.change(within(impact).getByRole("textbox"), { target: { value: "main..feature" } });
    fireEvent.click(within(impact).getByRole("button", { name: "Work out the impact" }));
    expect(await within(impact).findByText("Cited by one record.")).toBeDefined();
    expect(server.called("analyze_change_impact")!.body).toEqual({
      scope,
      repositoryId: REPOSITORY,
      commitOrRange: "main..feature",
    });

    const compare = screen.getByRole("heading", { name: "Compare two references" }).closest("section")!;
    const [earlier, later] = within(compare).getAllByRole("textbox");
    fireEvent.change(earlier!, { target: { value: "v1" } });
    fireEvent.change(later!, { target: { value: "v2" } });
    fireEvent.click(within(compare).getByRole("button", { name: "Compare" }));
    expect(await within(compare).findByText("Pack files are not read.")).toBeDefined();
    expect(server.called("compare_snapshots")!.body).toEqual({
      scope,
      repositoryId: REPOSITORY,
      earlierReference: "v1",
      laterReference: "v2",
    });

    const sync = screen.getByRole("heading", { name: "Synchronise a repository" }).closest("section")!;
    fireEvent.click(within(sync).getByRole("button", { name: "Synchronise" }));

    // Absent is not zero: a working copy cannot say how many pull requests are open.
    expect((await within(sync).findAllByText("Not available from a working copy")).length).toBe(2);
    expect(server.called("sync_sources")!.body).toEqual({ scope, repositoryId: REPOSITORY });
  });

  test("the maintenance screen runs the sweeps, the index, the text checks and the export", async () => {
    signedIn();
    render(mount(`${project}/maintenance`));

    fireEvent.click(await screen.findByRole("button", { name: "Check provenance" }));
    expect(await screen.findByText("No source locator.")).toBeDefined();

    fireEvent.click(screen.getByRole("button", { name: "Find duplicates" }));
    fireEvent.click(screen.getByRole("button", { name: "Find stale records" }));
    await waitFor(() => expect(server.called("detect_staleness")).toBeDefined());
    expect(server.called("detect_duplicates")!.body).toEqual({ scope });
    expect(server.called("detect_staleness")!.body).toEqual({ scope, staleAfter: "180.00:00:00" });

    fireEvent.click(screen.getByRole("button", { name: "Rebuild the index" }));
    expect(await screen.findByText("7 record(s) indexed.")).toBeDefined();

    fireEvent.change(textbox(/^Text/), { target: { value: "key: AKIA..." } });
    fireEvent.click(screen.getByRole("button", { name: "Look for secrets" }));
    expect(await screen.findByText("aws-access-key")).toBeDefined();
    fireEvent.click(screen.getByRole("button", { name: "Redact it" }));
    expect(await screen.findByText("key: [REDACTED]")).toBeDefined();
    expect(server.called("redact_sensitive_data")!.body).toEqual({ scope, content: "key: AKIA..." });

    fireEvent.click(screen.getByRole("button", { name: "Export this project" }));
    expect(await screen.findByText("export-20260902")).toBeDefined();
    expect(server.called("export_project")!.body).toEqual({ scope });
  });

  test("a contributor is offered analysis and not maintenance", async () => {
    server.restore();
    server = fakeServer(["ReadKnowledge", "AnalyzeProject", "CreateDraft", "ManageWorkItems"]);

    signedIn();
    render(mount(project));

    const nav = await screen.findByRole("navigation", { name: "Project" });
    expect(within(nav).getByRole("link", { name: "Analysis" })).toBeDefined();
    expect(within(nav).getByRole("link", { name: "Search" })).toBeDefined();
    expect(within(nav).queryByRole("link", { name: "Maintenance" })).toBeNull();
  });

  test("a viewer is offered neither analysis nor maintenance", async () => {
    server.restore();
    server = fakeServer(["ReadKnowledge"]);

    signedIn();
    render(mount(project));

    const nav = await screen.findByRole("navigation", { name: "Project" });
    expect(within(nav).queryByRole("link", { name: "Analysis" })).toBeNull();
    expect(within(nav).queryByRole("link", { name: "Maintenance" })).toBeNull();
  });

  test("a contributor on the analysis screen is not offered synchronisation", async () => {
    server.restore();
    server = fakeServer(["ReadKnowledge", "AnalyzeProject"]);

    signedIn();
    render(mount(`${project}/analysis`));

    await screen.findByRole("heading", { name: "What a change affects" });
    expect(screen.queryByRole("heading", { name: "Synchronise a repository" })).toBeNull();
  });

  test("a backup is taken from the health screen", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/health`));

    fireEvent.click(await screen.findByRole("button", { name: "Back up now" }));
    expect(await screen.findByText("backup-20260902")).toBeDefined();
    expect(server.called("backup_system")!.body).toEqual({ workspaceId: WORKSPACE });
  });

  test("changing somebody's role revokes the old grant, then grants the new one on the same scope", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/members`));

    // Not offered on the signed-in person's own grant.
    await screen.findByRole("combobox", { name: `New role for ${OTHER_USER}` });
    expect(screen.queryByRole("combobox", { name: `New role for ${USER}` })).toBeNull();

    fireEvent.change(screen.getByRole("combobox", { name: `New role for ${OTHER_USER}` }), {
      target: { value: "Reviewer" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Change role" }));

    await waitFor(() => expect(server.called("grant_membership")).toBeDefined());

    const order = server.calls
      .map((call) => call.operation)
      .filter((operation) => operation === "revoke_membership" || operation === "grant_membership");
    expect(order).toEqual(["revoke_membership", "grant_membership"]);

    expect(server.called("revoke_membership")!.body).toEqual({
      workspaceId: WORKSPACE,
      membershipId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
    });
    expect(server.called("grant_membership")!.body).toEqual({
      workspaceId: WORKSPACE,
      subjectUserId: OTHER_USER,
      role: "Reviewer",
      scopedToProject: null,
    });
  });

  test("somebody already here can be given a grant on one project", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/members`));

    const panel = (await screen.findByRole("heading", { name: "Grant access to somebody already here" })).closest(
      "section",
    )!;
    const [person, role, where] = within(panel).getAllByRole("combobox");

    fireEvent.change(person!, { target: { value: OTHER_USER } });
    fireEvent.change(role!, { target: { value: "Administrator" } });
    await within(panel).findByRole("option", { name: "Alpha" });
    fireEvent.change(where!, { target: { value: PROJECT } });
    fireEvent.click(within(panel).getByRole("button", { name: "Grant" }));

    expect(await within(panel).findByText("Granted Administrator.")).toBeDefined();
    expect(server.called("grant_membership")!.body).toEqual({
      workspaceId: WORKSPACE,
      subjectUserId: OTHER_USER,
      role: "Administrator",
      scopedToProject: PROJECT,
    });
  });
});
