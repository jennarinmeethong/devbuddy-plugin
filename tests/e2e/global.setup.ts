import { expect, test as setup } from "@playwright/test";
import { Api } from "./support/api";
import { env } from "./support/env";
import { invite, saveCast } from "./support/people";

setup("create one account per role in the bootstrapped workspace", async () => {
  const admin = await Api.signIn(env.adminEmail, env.adminPassword);

  try {
    const me = await admin.me();

    // bootstrap makes exactly one workspace and its administrator. A stack that has more was not
    // started by run.sh, and the tests would be guessing which workspace to work in.
    const workspaces = [...new Map(me.workspaces.map((grant) => [grant.workspaceId, grant])).values()];
    expect(workspaces, "the administrator should hold exactly the bootstrapped workspace").toHaveLength(1);

    const workspace = workspaces[0]!;
    expect(workspace.role).toBe("Administrator");

    saveCast({
      workspaceId: workspace.workspaceId,
      workspaceName: workspace.name,
      admin: {
        userId: me.userId,
        email: env.adminEmail,
        password: env.adminPassword,
        displayName: me.displayName,
        role: "Administrator",
      },
      viewer: await invite(admin, workspace.workspaceId, "Viewer"),
      contributor: await invite(admin, workspace.workspaceId, "Contributor"),
      reviewer: await invite(admin, workspace.workspaceId, "Reviewer"),
    });
  } finally {
    await admin.dispose();
  }
});
