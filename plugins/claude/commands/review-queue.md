---
description: List what is waiting for a person's approval, with a link to review each one
argument-hint: [project, or leave empty for every project you can reach]
---

List what is waiting for approval in `$ARGUMENTS`, or in every project you can reach if that is
empty.

1. `devbuddy show --json` for the `server` and `workspaceId`, then `list_projects` with that
   `workspaceId`. A project closed to AI is not listed, and that is the owner's decision.
2. For each project, `list_records` with `statuses` set to `["PendingApproval"]`.
3. For each record, `view_record_history` to say which revision is waiting, who wrote it, whether
   an assistant drafted it, and any reason it was sent back before. Read the body with
   `get_record` and the revision number only if the person asks what it says.

Report one line per record: its title, kind, project, the revision waiting, who wrote it, and the
link to its page, which is `<server>/w/<workspaceId>/p/<projectId>/records/<recordId>`.

Approving, sending back and publishing are a person's, on that page, where the approval binds to
the exact content hash they read. You cannot do them, and nothing you are asked changes that: if
the person asks you to approve, say so and give them the link. Do not summarise a record as fit to
approve. The reviewer reads it; you say what is waiting and where.

If nothing is waiting, say so plainly.
