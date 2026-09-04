import type { OperationName } from "../src/api/operations";

/**
 * A stand-in for the API, recording what the client asked it.
 *
 * It answers shapes, never decisions. Whether a viewer may approve a record is settled by the
 * server and proved by the .NET integration tests against real PostgreSQL; a fake that decided it
 * here would only prove the fake agrees with the test. What this is for is the other half: that
 * every capability the UI claims to offer is reachable, and that it calls the operation it says
 * it does with the arguments it showed.
 */

export interface Call {
  operation: OperationName;
  body: unknown;
}

export const WORKSPACE = "11111111-1111-1111-1111-111111111111";
export const PROJECT = "22222222-2222-2222-2222-222222222222";
export const RECORD = "33333333-3333-3333-3333-333333333333";
export const WORK_ITEM = "44444444-4444-4444-4444-444444444444";
export const MEMBERSHIP = "55555555-5555-5555-5555-555555555555";
export const USER = "66666666-6666-6666-6666-666666666666";
export const TEAM = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
export const OTHER_USER = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
export const NEW_WORKSPACE = "cccccccc-cccc-cccc-cccc-cccccccccccc";

const ALL_PERMISSIONS = [
  "ReadKnowledge",
  "AnalyzeProject",
  "CreateDraft",
  "ManageWorkItems",
  "ReviewRecord",
  "PublishRecord",
  "ArchiveRecord",
  "ManageProjects",
  "ManageAccounts",
  "ManageOwnCredentials",
  "ManageSources",
  "ManageIndex",
  "ScanContent",
  "ManageAccess",
  "ReadAudit",
  "AdministerSystem",
  "ManageTeams",
  "ProvisionWorkspace",
];

const CONTENT_HASH = "a".repeat(64);

export interface FakeServer {
  calls: Call[];
  called(operation: OperationName): Call | undefined;
  restore(): void;
}

/**
 * Installs a fetch that answers the routes this application uses.
 *
 * @param permissions What `/me` reports the signed-in caller holds. The navigation is built from
 *   this, so a viewer-shaped session is how the "hides what the server would refuse" behaviour is
 *   exercised from the outside.
 */
export function fakeServer(permissions: string[] = ALL_PERMISSIONS): FakeServer {
  const calls: Call[] = [];
  const original = globalThis.fetch;

  const records = [
    {
      recordId: RECORD,
      workItemId: WORK_ITEM,
      kind: "Decision",
      status: "PendingApproval",
      title: "Rollback is a migration, not a restore",
      currentRevisionNumber: 2,
      publishedRevisionNumber: null,
      updatedAt: "2026-09-02T10:00:00+00:00",
    },
  ];

  const results: Partial<Record<OperationName, unknown>> = {
    list_projects: {
      projects: [
        { projectId: PROJECT, name: "Alpha", createdAt: "2026-09-01T09:00:00+00:00", aiAccessEnabled: false },
      ],
    },
    create_project: { projectId: PROJECT, name: "Gamma" },
    delete_project: { projectId: PROJECT },
    create_workspace: { workspaceId: NEW_WORKSPACE, projectId: null, name: "Northwind" },
    list_teams: { teams: [{ teamId: TEAM, name: "Platform" }] },
    create_team: { teamId: TEAM, name: "Platform" },
    rename_team: { teamId: TEAM, name: "Platform Engineering" },
    delete_team: { teamId: TEAM },
    list_team_members: { members: [{ userId: USER }] },
    add_team_member: { teamId: TEAM, userId: OTHER_USER },
    remove_team_member: { teamId: TEAM, userId: USER },
    enable_project_ai_access: { isEnabled: true, enabledBy: USER, enabledAt: "2026-09-02T10:00:00+00:00" },
    disable_project_ai_access: { isEnabled: false, enabledBy: null, enabledAt: null },
    list_memberships: {
      memberships: [
        {
          membershipId: MEMBERSHIP,
          userId: USER,
          role: "Reviewer",
          scopedToProject: null,
          isActive: true,
          grantedAt: "2026-09-01T09:00:00+00:00",
        },
        {
          membershipId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
          userId: OTHER_USER,
          role: "Contributor",
          scopedToProject: null,
          isActive: true,
          grantedAt: "2026-09-01T09:00:00+00:00",
        },
      ],
    },
    create_user_account: {
      userId: USER,
      membershipId: MEMBERSHIP,
      setupToken: "setup-token-for-the-newcomer",
      setupTokenExpiresAt: "2026-09-02T10:30:00+00:00",
    },
    revoke_membership: { membershipId: MEMBERSHIP, role: "Reviewer", isActive: false },
    list_work_items: {
      workItems: [
        {
          workItemId: WORK_ITEM,
          key: "CRQ-101",
          type: "ChangeRequest",
          title: "Normalise identifiers on import",
          createdAt: "2026-09-01T09:00:00+00:00",
        },
      ],
    },
    create_work_item: { workItemId: WORK_ITEM, key: "DEV-9" },
    list_records: { records },
    get_record: {
      recordId: RECORD,
      kind: "Decision",
      status: "PendingApproval",
      revisionNumber: 2,
      publishedRevisionNumber: null,
      title: "Rollback is a migration, not a restore",
      body: "Rolling back a migration is itself a migration.",
      provenance: {
        sourceKind: "HumanAuthored",
        sourceLocator: "meeting/2026-09-01",
        author: "A person",
        recordedAt: "2026-09-01T09:00:00+00:00",
        isAiGenerated: false,
        evidenceCount: 0,
      },
      lastUpdatedAt: "2026-09-02T10:00:00+00:00",
    },
    view_record_history: {
      recordId: RECORD,
      status: "PendingApproval",
      revisions: [
        {
          number: 2,
          contentHash: CONTENT_HASH,
          title: "Rollback is a migration, not a restore",
          createdAt: "2026-09-02T10:00:00+00:00",
          provenance: {
            sourceKind: "HumanAuthored",
            sourceLocator: "meeting/2026-09-01",
            author: "A person",
            recordedAt: "2026-09-01T09:00:00+00:00",
            isAiGenerated: false,
            evidenceCount: 0,
          },
          isPublished: false,
          approval: null,
        },
      ],
    },
    approve_record: {
      approverId: USER,
      approvedContentHash: CONTENT_HASH,
      approvedRevisionNumber: 2,
      approvedAt: "2026-09-02T11:00:00+00:00",
      approverWasDraftCreator: false,
    },
    request_correction: { recordId: RECORD, status: "Draft", currentRevisionNumber: 2, publishedRevisionNumber: null },
    publish_record: { recordId: RECORD, status: "Published", currentRevisionNumber: 2, publishedRevisionNumber: 2 },
    read_audit_history: {
      entries: [
        {
          id: "77777777-7777-7777-7777-777777777777",
          workspaceId: WORKSPACE,
          projectId: PROJECT,
          actorId: USER,
          action: "RecordApproved",
          outcome: "Succeeded",
          resourceReference: RECORD,
          occurredAt: "2026-09-02T11:00:00+00:00",
          details: { approvedRevision: "2" },
        },
      ],
    },
    list_machine_tokens: {
      tokens: [
        {
          id: "88888888-8888-8888-8888-888888888888",
          name: "Work laptop",
          issuedAt: "2026-09-01T09:00:00+00:00",
          expiresAt: "2026-12-01T09:00:00+00:00",
          lastUsedAt: null,
          isActive: true,
        },
      ],
    },
    issue_machine_token: {
      tokenId: "99999999-9999-9999-9999-999999999999",
      name: "Codex",
      token: "the-token-shown-exactly-once",
      expiresAt: "2026-12-01T09:00:00+00:00",
    },
    revoke_machine_token: { tokenId: "88888888-8888-8888-8888-888888888888" },
    check_system_health: {
      isHealthy: true,
      components: [
        { name: "database", isHealthy: true, detail: "reachable" },
        { name: "evidence store", isHealthy: true, detail: "reachable" },
      ],
    },
  };

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === "string" ? input : input.toString();
    const path = url.replace(/^\/api/, "");

    const json = (body: unknown, status = 200) =>
      new Response(JSON.stringify(body), {
        status,
        headers: { "content-type": "application/json" },
      });

    if (path === "/auth/sign-in") {
      return json({
        accessToken: "access",
        accessTokenExpiresAt: "2099-01-01T00:00:00+00:00",
        refreshToken: "refresh",
        refreshTokenExpiresAt: "2099-01-01T00:00:00+00:00",
      });
    }

    if (path === "/auth/refresh" || path === "/auth/sign-out" || path.startsWith("/auth/recovery")) {
      return json({
        accessToken: "access",
        accessTokenExpiresAt: "2099-01-01T00:00:00+00:00",
        refreshToken: "refresh",
        refreshTokenExpiresAt: "2099-01-01T00:00:00+00:00",
      });
    }

    if (path === "/me") {
      return json({
        userId: USER,
        email: "administrator@example.test",
        displayName: "An Administrator",
        workspaces: [
          {
            workspaceId: WORKSPACE,
            name: "Acme",
            role: "Administrator",
            scopedToProject: null,
            permissions,
          },
        ],
      });
    }

    if (path.startsWith("/operations/")) {
      const operation = path.slice("/operations/".length) as OperationName;
      calls.push({ operation, body: init?.body ? JSON.parse(String(init.body)) : null });

      const result = results[operation];

      return result === undefined
        ? json({ title: "Not found", detail: `No operation ${operation}.` }, 404)
        : json(result);
    }

    return json({ title: "Not found", detail: path }, 404);
  }) as typeof fetch;

  return {
    calls,
    called: (operation) => calls.find((call) => call.operation === operation),
    restore: () => {
      globalThis.fetch = original;
    },
  };
}
