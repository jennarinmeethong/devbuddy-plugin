import { afterEach, beforeEach, describe, expect, test } from "bun:test";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactElement } from "react";
import { SessionProvider } from "../src/api/session";
import { App } from "../src/App";
import { forgetTokens } from "../src/api/client";
import { fakeServer, PROJECT, RECORD, WORKSPACE } from "./server";
import type { FakeServer } from "./server";

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
    expect(server.called("list_memberships")).toBeDefined();
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

    fireEvent.click(await screen.findByRole("button", { name: "Revoke" }));

    await waitFor(() => expect(server.called("revoke_membership")).toBeDefined());
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

    expect(within(panel).getByText(/Revision 2/)).toBeDefined();
    expect(within(panel).getByText("a".repeat(64))).toBeDefined();
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

describe("audit and health", () => {
  test("the audit trail is readable per project", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/audit`));

    fireEvent.change(await screen.findByRole("combobox", { name: "Project" }), {
      target: { value: PROJECT },
    });

    await waitFor(() => expect(server.called("read_audit_history")).toBeDefined());
    expect(await screen.findByText("RecordApproved")).toBeDefined();
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

    fireEvent.change(await screen.findByPlaceholderText("Work laptop, Codex"), {
      target: { value: "Codex" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Mint" }));

    await waitFor(() => expect(server.called("issue_machine_token")).toBeDefined());
    expect(await screen.findByText("the-token-shown-exactly-once")).toBeDefined();
  });

  test("a token can be revoked", async () => {
    signedIn();
    render(mount(`/w/${WORKSPACE}/plugin-access`));

    fireEvent.click(await screen.findByRole("button", { name: "Revoke" }));

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
