import type { Topic, TourStep } from "../components/Guide";
import { m } from "../i18n";

/**
 * What the tours and the "?" hints say, screen by screen.
 *
 * Kept apart from the screens so a screen carries only the `data-tour` names its steps point at.
 * Every sentence is an English key marked with `m`, with its Thai in `th.ts`; `i18n.test.tsx`
 * holds the two equal. A blank line in a body separates paragraphs.
 *
 * What these say has to stay true of the server, not only of the screen: a hint that promised
 * something the server does not do would be worse than no hint. Where a screen hides a part from a
 * role, the tour skips its step, so no step may be the only place something essential is said.
 */

const WHERE_NEXT: TourStep = {
  title: m("That is this screen"),
  body: m(
    "Every screen has its own tour under the same button. A ? beside a heading or a field explains that one part in more detail.",
  ),
};

export const HINTS = {
  // Workspace and projects
  aiAccess: {
    title: m("AI access to a project"),
    body: m(
      "Whether Claude, Codex or Cowork may read this project through the DevBuddy plugin. It is denied by default, and a project nobody opened stays closed.\n\nEnabled, an assistant works as the person whose machine token it uses, with exactly that person's role and no more. It can search, read, analyse, write a draft and prepare a handover. It cannot approve, publish, archive, attach evidence or manage anybody: those stay with people, on these screens.\n\nDenied, the project is not even listed to an assistant, and every call naming it is refused. Denying takes effect on the next call. It cannot recall what an assistant already read.",
    ),
  },
  boundedScope: {
    title: m("Bounded scope for personal data"),
    body: m(
      "On the AI channel, personal data is blocked from drafts and redacted from what an assistant reads: email addresses, ID and card numbers, Thai mobile numbers and labelled personal details. People are never subject to this rule.\n\nA bounded scope names the kinds an assistant may see on this one project, with the reason it is approved. Everything not ticked stays blocked and redacted. You can withdraw the scope at any time.\n\nSecrets — passwords, tokens, keys, connection strings — are refused whatever the scope says. There is no setting that lets one through.",
    ),
  },
  deleteProject: {
    title: m("Deleting a project"),
    body: m(
      "Deleting removes the project's work items, every record with its whole revision history, its evidence files and its project-level grants, at once. There is no undo, and a later restore of an older backup deletes it again.\n\nThat is why you type the project's name back: a confirmation that can be clicked through by habit is not a confirmation.\n\nThe audit history survives. Deleting content is not the same as erasing that the deletion happened.",
    ),
  },
  role: {
    title: m("Roles"),
    body: m(
      "Viewer reads the knowledge, work items and evidence in reach, and can mint their own plugin tokens.\n\nContributor can also run analyses, register work, write and revise drafts, submit them for approval and attach evidence.\n\nReviewer can also approve a revision or send it back, publish and archive.\n\nAdministrator can also manage projects, people, teams, sources and AI access, read the audit trail, run maintenance and back up.\n\nIndex maintainer is for a worker account: it reads and keeps the search index, and nothing else. The first four are cumulative; this one is not above Administrator.",
    ),
  },
  grantScope: {
    title: m("Where a grant applies"),
    body: m(
      "A grant on the whole workspace covers every project in it, including ones created later. A grant on one project covers that project only.\n\nSomebody can hold several grants, such as Viewer on the workspace and Reviewer on one project. Their permission on a project is the widest grant that covers it.",
    ),
  },
  changeRole: {
    title: m("Changing somebody's role"),
    body: m(
      "A grant's role cannot be edited, so a change is two steps: the old grant is revoked, then a new one is made for the same place.\n\nRevoking comes first because it fails safe. If the second step is refused, the person has less access than intended, not more, and the screen tells you to grant it again.\n\nYou cannot change your own grant here: the second step would need the permission the first one took away.",
    ),
  },
  revoked: {
    title: m("When access ends"),
    body: m(
      "A revoked grant stays in the list, marked Revoked, so the list shows what access somebody had and when it ended.\n\nRevoking takes effect on the person's next request, not their next sign-in. It cannot recall anything they already downloaded: Downloads lists what they downloaded or exported in the last 90 days, so you can follow it up outside the system.",
    ),
  },
  resetPassword: {
    title: m("Issuing a password reset"),
    body: m(
      "For somebody who cannot receive the recovery email: the installation has no mail server, or they lost the address.\n\nThe token is shown once, here, is single-use and expires. Give it to them by a channel you trust; they redeem it on the set-password page, and their other sessions end.\n\nIt is refused if they also belong to a workspace you do not administer, because a password works in every workspace they are in.",
    ),
  },
  setupToken: {
    title: m("The setup token"),
    body: m(
      "A new account cannot be signed into until its owner sets a password with this token.\n\nThe token is shown once and is not kept on this page. When the installation has SMTP configured it is also emailed to the address; this page cannot tell whether that delivery worked, so it shows the token either way. It is single-use and expires.",
    ),
  },
  team: {
    title: m("What a team is"),
    body: m(
      "A team is a named group of people, for knowing who works together. It carries no role and no permission.\n\nWhat anybody may do is decided by their grants on the Members screen, whether or not they are on a team. To add somebody to a team they need an account and a grant on this workspace first.",
    ),
  },
  sponsor: {
    title: m("Creating another workspace"),
    body: m(
      "Each workspace is a separate tenant: projects, records, evidence, members and tokens are never shared between two.\n\nA new workspace is sponsored by this one: you need to administer this workspace to create it, and you become the new one's administrator. There is no installation-wide administrator above workspaces.",
    ),
  },
  channel: {
    title: m("Channel"),
    body: m(
      "How the request arrived. Person: the web screens, the API or the console. Assistant: Claude, Codex or Cowork through the plugin. Internal system: the server's own scheduled work, such as the retention sweep and the bootstrap.\n\nAn assistant acts with a person's machine token, so the actor is that person either way. The channel is what tells the two apart.\n\nEntries from before v1.3.0 have no channel. They are shown as not recorded, never guessed.",
    ),
  },
  auditEntry: {
    title: m("What an audit entry holds"),
    body: m(
      "Who did what, to which resource, on which channel, when, and whether it was allowed. Refusals are recorded as well as successes.\n\nAn entry never holds content: no record body, no evidence, no secret. The detail column carries identifiers and counts only, so the audit trail is not a second copy of the knowledge.",
    ),
  },
  backup: {
    title: m("Backup and restore"),
    body: m(
      "Back up now writes every workspace — rows and evidence files — to the server's backup volume. Copy it off that volume to keep it safe from losing the disk.\n\nRestoring is not on any screen. It is a console command run by whoever operates the server, because a restore from total loss runs on a database with no accounts left to check.\n\nBackups older than the retention period are removed by the retention sweep.",
    ),
  },
  machineToken: {
    title: m("Machine tokens"),
    body: m(
      "What the devbuddy client gives Claude Code, Codex or Cowork, so the assistant can call DevBuddy as you.\n\nIt carries your permissions and no more, in this workspace only. Revoking it stops the next call. Only a hash is stored, so the value is shown once; a lost token is replaced, never recovered.\n\nThe client keeps it in the operating system's credential store, per registered folder, and hands it only to the server that folder is registered to.",
    ),
  },
  registerCheckout: {
    title: m("Connecting a folder"),
    body: m(
      "The command ties a folder on your machine to this server and this workspace. Run it in the folder where you start Claude Code or Codex, then store the token with devbuddy token set.\n\nAdding --project makes that project the assistant's default. A session uses the workspace of the folder it was started in, and changing folder later does not change it.\n\ndevbuddy doctor checks the registration, the stored token, the server's certificate and that the token works here.",
    ),
  },

  // Work and records
  workType: {
    title: m("Types of work"),
    body: m(
      "Develop: new work. Enhance: improving something that exists. Fix a bug: a defect, its cause and the fix.\n\nChange request and Code review are separate types and are never abbreviated to one name. A change request changes agreed scope; a code review is feedback on code and what was done about it.",
    ),
  },
  recordKind: {
    title: m("Kinds of record"),
    body: m(
      "Decision: what was decided, why, the alternatives and who approved it. Technical knowledge: architecture, data flows, APIs and configuration worth knowing. Change impact: what a change affected, risks, migrations and rollback. Handover: what the next owner needs. Code review feedback: what a review said and what was done about it. Context reference: links to issues, documents and environments. Delivery state: status, acceptance criteria and how it was verified.",
    ),
  },
  provenance: {
    title: m("Provenance"),
    body: m(
      "Where a record's content came from, so a later reader can check it against its origin: a meeting, a document, a commit, a ticket.\n\nWhere it came from says what kind of source it was. Source is where to find it. Your name and the time are recorded for you.\n\nWhether an AI wrote it is not chosen here: the server marks anything an assistant sends through the plugin. Choose Written by an AI assistant when you are pasting what an assistant wrote somewhere else.",
    ),
  },
  frontMatter: {
    title: m("Front matter"),
    body: m(
      "Named fields kept with the body, such as owner, component or ticket. Each name may appear once.\n\nThey are part of the content an approval binds to, which is why they are shown beside the body: a reviewer approves the fields as well as the text.",
    ),
  },
  draftEvidence: {
    title: m("Linking evidence to a draft"),
    body: m(
      "Tick the evidence that backs this record and say what each shows. Evidence is attached on the project's Evidence screen first; here you only link it.\n\nThe links are kept with every later revision. Find missing evidence on a work item points at claims nothing backs up.",
    ),
  },
  handover: {
    title: m("Handing work over"),
    body: m(
      "Generate a handover assembles what is published for this work, kind by kind, with the open questions and missing evidence. Drafts are not included: a draft is not knowledge yet.\n\nFind open questions and Find missing evidence run the two checks on their own. Each reads every record of the work, so they run when you ask rather than on every visit.",
    ),
  },
  lifecycle: {
    title: m("How a record becomes knowledge"),
    body: m(
      "Draft: written by a person or an assistant, and not knowledge yet. Handovers and search by meaning leave it out, and a text search shows it only when Draft is ticked.\n\nWaiting for approval: submitted. A reviewer approves one exact revision or sends it back with a reason.\n\nApproved: a person agreed to that exact content. Published: readers of published knowledge, search and assistants now see it.\n\nArchived: out of use for good. Its history stays readable.",
    ),
  },
  contentHash: {
    title: m("Why the approval names a hash"),
    body: m(
      "The long code is the content hash of the revision on screen: title, body and front matter together.\n\nApproving sends that hash. If anybody saves a new revision while you are reading, the hash no longer matches and the server refuses the approval, so you can never approve content you were not shown. Read the new revision and approve that.\n\nYou may approve a draft you wrote yourself; the history records that you did.",
    ),
  },
  publishedVersusCurrent: {
    title: m("Published content and newer revisions"),
    body: m(
      "Readers see the published revision. A record can be revised after it is published: the newer revision is shown separately, as not published, until it is approved and published in turn.\n\nRevisions are never changed once saved. Editing adds a new revision and keeps the old one in the history.",
    ),
  },
  markAi: {
    title: m("Recording that an AI wrote a record"),
    body: m(
      "For a record an assistant wrote that was stored as a person's work, which could happen before 15 September 2026, when nothing recorded the channel a draft came from.\n\nEvery revision is marked, with your name, the time and your reason beside the mark. Nothing removes it. The content and its approvals are not changed.",
    ),
  },
  archive: {
    title: m("Archiving"),
    body: m(
      "Archiving takes a record out of use. It can no longer be revised, approved or published, it leaves semantic search, and nothing brings it back.\n\nIts history stays readable. To replace a record, write a new draft and archive the old one once the new one is published.",
    ),
  },
  evidenceState: {
    title: m("Evidence state"),
    body: m(
      "Every file is scanned when it is attached, and one that fails the scan is refused rather than kept, so a file attached on this screen is Clean: scanned, nothing found, and it can be downloaded.\n\nRedacted means sensitive parts were removed before it was kept, and it can be downloaded too. Not scanned means no scan was recorded, and it is never released until one is. Blocked is never released.\n\nDownload is offered only for evidence that can be released.",
    ),
  },
  evidenceScan: {
    title: m("Scanning a new file"),
    body: m(
      "The file is scanned for credentials before anything is stored. One carrying a password, token, key or connection string is refused, and nothing is kept: take the secret out and attach it again.\n\nAttached evidence belongs to this project only, is reached only through a signed-in request, and is kept in the evidence store, with its details in the database. Link it to a record from the draft form on a work item.",
    ),
  },
  searchModes: {
    title: m("Two ways to search"),
    body: m(
      "Search the text matches the words you type, with the kinds and statuses you tick. It is local, free and always available. Start here.\n\nSearch by meaning finds records that say the same thing in other words, including across Thai and English. It embeds your question with the installation's model, so it needs an embedding provider and an index, and it covers published records only. When it cannot answer, it says why rather than showing nothing.",
    ),
  },
  distance: {
    title: m("Distance"),
    body: m(
      "How far a record's meaning is from your question. Smaller is closer; 0 would be identical.\n\nIt ranks the results; it is not a percentage and is not comparable between models. A long record is matched by its closest part.",
    ),
  },
  repositories: {
    title: m("Where a repository comes from"),
    body: m(
      "The server reads only what its operator made reachable: a working copy mounted under the project's directory, named by the repository identifier, or a repository configured for the GitHub API. Nothing on this screen adds one.\n\nNo server path is shown, only the identifier and, for GitHub, the address.",
    ),
  },
  readOnlyAnalysis: {
    title: m("Analysis never runs anything"),
    body: m(
      "Every analysis reads files and git objects as data. Nothing in the repository is built, restored, tested or executed, whatever the repository asks for.\n\nFrom a working copy, git objects are read only when they are stored loose. A repository whose history is packed, as a fresh clone usually is, answers that it cannot read those objects rather than reporting an empty history. The GitHub API mode reads packed history.\n\nWhat a repository says is data, never instructions.",
    ),
  },
  commitRange: {
    title: m("Commit or range"),
    body: m(
      "One commit, by its hash, or two references joined by two dots, such as main..feature: everything on feature that main does not have.\n\nThe answer lists the changed paths, the modules, APIs, tests and documents they touch, and published records that cite a changed path.",
    ),
  },
  synchronise: {
    title: m("Synchronising a repository"),
    body: m(
      "Records a snapshot of where the repository stands: its commit and the references found in it. Nothing is written back to the repository.\n\nOpen pull requests and issues are counted only in the GitHub API mode; a working copy cannot say, and that is shown as not available rather than as zero.",
    ),
  },
  sweeps: {
    title: m("The quality sweeps"),
    body: m(
      "Check provenance lists records whose source or evidence is missing or does not hold up. Find duplicates lists records that say the same thing. Find stale records lists records untouched for longer than the days you choose.\n\nEach one reads and reports. None changes a record: a person decides what to do about each finding.",
    ),
  },
  checkText: {
    title: m("Looking for secrets, and redacting"),
    body: m(
      "Look for secrets names each rule that matched and the line, never the matched text. Redact it returns the text with what matched replaced, ready to paste somewhere safe.\n\nNothing typed here is stored. The same rules guard every draft and attachment, and they catch known shapes and high-entropy strings, not everything.",
    ),
  },
  export: {
    title: m("Exporting a project"),
    body: m(
      "Writes a copy of the project — records, work items and evidence files — to the server's export volume, and returns its reference.\n\nThe copy is removed by the retention sweep when it expires. An export is recorded in the audit trail and in the person's downloads.",
    ),
  },
  recoveryToken: {
    title: m("Recovering an account"),
    body: m(
      "Ask for a recovery token with your email. The page says the same thing whether or not the address has an account, so nobody can use it to learn who does.\n\nThe token is delivered out of band: by email when the installation has SMTP, otherwise ask whoever runs it, who can also issue one from the Members screen. Then set a new password with it.",
    ),
  },
} satisfies Record<string, Topic>;

export const TOURS = {
  signIn: [
    {
      title: m("Signing in"),
      body: m(
        "DevBuddy keeps what a team worked out — decisions, technical knowledge, change impact and handovers — so whoever picks the work up next can trust it. This page is how a person signs in to it.",
      ),
    },
    {
      target: "sign-in-form",
      title: m("Your account"),
      body: m(
        "Sign in with the email and password of your DevBuddy account. DevBuddy has its own accounts; it does not sign in with GitHub, Microsoft or an assistant's account.\n\nToo many wrong passwords lock the account for a while.",
      ),
    },
    {
      target: "sign-in-recover",
      title: m("Forgot your password?"),
      body: m("Switches the form to account recovery, which issues a recovery token for your address."),
    },
    {
      target: "sign-in-token",
      title: m("A token in hand"),
      body: m(
        "A new account's setup token, or a recovery token, is redeemed on the set-password page. Follow this link when you have one.",
      ),
    },
  ],
  setPassword: [
    {
      target: "set-password-form",
      title: m("Setting a password"),
      body: m(
        "Paste the setup or recovery token you were given, then choose a password of at least twelve characters. Length is the only rule.\n\nSetting it signs out every other session of the account. The token works once.",
      ),
    },
  ],
  workspacePicker: [
    {
      target: "workspace-picker",
      title: m("Your workspaces"),
      body: m(
        "Every workspace you have a grant on, with your role in each. A workspace is a separate tenant: nothing in one is visible from another.\n\nOpen one to work in it. If none is listed, an administrator has to grant you access.",
      ),
    },
  ],
  projects: [
    {
      title: m("The workspace"),
      body: m(
        "This is the home of a workspace. Knowledge lives in projects, and every screen after this one works inside one project.",
      ),
    },
    {
      target: "workspace-nav",
      title: m("The menu"),
      body: m(
        "The workspace's screens. It lists only what your role may use, so a viewer sees fewer entries than an administrator. Hiding a screen is a courtesy: the server checks your permission again on every request.",
      ),
    },
    {
      target: "role",
      title: m("Your role here"),
      body: m(
        "The role your grant gives you in this workspace. A grant on one project can give you more there. The ? on the Members screen explains each role.",
      ),
    },
    {
      target: "language",
      title: m("Language"),
      body: m(
        "Switches every screen between English and Thai, and is remembered in this browser. Names, record contents and the server's own messages are shown as they were written.",
      ),
    },
    {
      target: "projects-list",
      title: m("Projects"),
      body: m(
        "One row per project. Open a name to reach its work items, records, search, analysis and evidence. AI access says whether assistants may read the project at all.",
      ),
    },
    {
      target: "project-actions",
      title: m("A project's switches"),
      body: m(
        "Enable AI access opens the project to Claude, Codex and Cowork; Deny AI access closes it at once. Bounded scope, once access is enabled, decides which kinds of personal data an assistant may see. Delete removes the project for good, after you type its name.\n\nEach is offered only to a role that may use it.",
      ),
    },
    {
      target: "projects-new",
      title: m("A new project"),
      body: m("Creates a project in this workspace. It starts closed to AI and stays closed until somebody opens it."),
    },
    WHERE_NEXT,
  ],
  workItems: [
    {
      target: "project-nav",
      title: m("Inside a project"),
      body: m(
        "The project's screens: work items, knowledge records, search, analysis, evidence and maintenance. Like the main menu, it shows only what your role may use.",
      ),
    },
    {
      target: "work-items-list",
      title: m("Work items"),
      body: m(
        "A work item is one piece of work, known by its key, such as DEV-101. Every knowledge record belongs to one, so this is where knowledge starts.\n\nOpen a title to see the work, the records written for it, the form for a new draft and its handover.",
      ),
    },
    {
      target: "work-items-new",
      title: m("Registering work"),
      body: m(
        "Records what the work is: its key, type, title and goal, and what is in and out of scope. The goal and the exclusions are what a later owner most needs and least often finds written down.",
      ),
    },
    WHERE_NEXT,
  ],
  workItem: [
    {
      target: "work-item-facts",
      title: m("The work"),
      body: m("The work item's identity: goal, type, scope, exclusions, stakeholders and how many records it has."),
    },
    {
      target: "work-item-records",
      title: m("Its knowledge"),
      body: m(
        "Every record written for this work, in any status. Open one to read it, review it or take it through its next step.",
      ),
    },
    {
      target: "work-item-draft",
      title: m("Writing a draft"),
      body: m(
        "A draft records something worked out for this work. Choose its kind, say where it came from, write the body in Markdown, add front matter and link evidence.\n\nSaving opens the new record. Nobody reading published knowledge sees it until it has been submitted, approved and published.",
      ),
    },
    {
      target: "work-item-handover",
      title: m("Handing it over"),
      body: m(
        "Assembles what a person taking this work over needs: the published knowledge kind by kind, the open questions and the claims nothing backs up.",
      ),
    },
    WHERE_NEXT,
  ],
  records: [
    {
      target: "records-filter",
      title: m("The review queue"),
      body: m(
        "The list opens on what is waiting for approval, because that is where somebody is waiting on a reviewer. Choose another status, or every status, to see the rest.",
      ),
    },
    {
      target: "records-list",
      title: m("Records"),
      body: m(
        "Each record with its kind, status and revision. When the newest revision is not the published one, both numbers are shown.\n\nNew drafts are written from their work item, because every record belongs to one.",
      ),
    },
    WHERE_NEXT,
  ],
  record: [
    {
      target: "record-content",
      title: m("The content"),
      body: m(
        "The revision readers see, or the newest one if nothing is published yet. The line above the body says which revision it is, whether an AI drafted it, where it came from and who recorded it. The front matter and the linked evidence follow the body.",
      ),
    },
    {
      target: "record-unpublished",
      title: m("A newer revision"),
      body: m(
        "This record was revised after it was published. This is the revision under work, shown in full, because it is the one an approval would cover.",
      ),
    },
    {
      target: "record-revise",
      title: m("Revising a draft"),
      body: m(
        "Edits the newest revision. Saving adds a new revision and keeps this one in the history. If a reviewer sent it back, their reason is shown here.",
      ),
    },
    {
      target: "record-submit",
      title: m("Submitting"),
      body: m("Puts the draft in the review queue. It publishes nothing."),
    },
    {
      target: "record-approval",
      title: m("Approving or sending back"),
      body: m(
        "Approve binds to the exact revision on screen, by its content hash. Send it back with a reason, and the writer sees it when they revise.",
      ),
    },
    {
      target: "record-publish",
      title: m("Publishing"),
      body: m(
        "Makes the approved revision the one readers, search and assistants see. It is refused if the approval no longer covers the current content.",
      ),
    },
    {
      target: "record-archive",
      title: m("Archiving"),
      body: m("Takes the record out of use for good, after a confirmation. Its history stays readable."),
    },
    {
      target: "record-mark-ai",
      title: m("Marking AI authorship"),
      body: m("Records that an assistant wrote a record stored as a person's work. It cannot be undone."),
    },
    {
      target: "record-history",
      title: m("History"),
      body: m(
        "Every revision, newest first: when and by whom, where it came from, its content hash, who approved it and whether that person also wrote it, and every reason it was sent back. Nothing in it is ever rewritten.",
      ),
    },
  ],
  evidence: [
    {
      target: "evidence-list",
      title: m("The project's evidence"),
      body: m(
        "Files attached to this project — logs, screenshots, exports — with their type, size, when they were captured and their scan state. Download fetches a file through your signed-in session; there is no link to share.",
      ),
    },
    {
      target: "evidence-attach",
      title: m("Attaching a file"),
      body: m(
        "Choose a file and say what it shows. It is scanned before it is stored. Then link it to a record from the draft form on a work item.",
      ),
    },
    WHERE_NEXT,
  ],
  search: [
    {
      target: "search-ask",
      title: m("Asking"),
      body: m(
        "Type a question or some words. The kinds and statuses narrow a text search; with none ticked it searches everything you may read. Published is ticked to start with, because that is knowledge somebody approved.",
      ),
    },
    {
      target: "search-buttons",
      title: m("Two ways to search"),
      body: m(
        "Search the text matches words. Search by meaning finds records that say the same thing in other words. Try the text first.",
      ),
    },
    {
      target: "search-text-results",
      title: m("Text results"),
      body: m("Each hit with its kind, status and an excerpt. Open a title to read the record."),
    },
    {
      target: "search-meaning-results",
      title: m("Results by meaning"),
      body: m(
        "The closest published records, smallest distance first, or the server's reason when it cannot search this way.",
      ),
    },
    WHERE_NEXT,
  ],
  analysis: [
    {
      target: "analysis-repositories",
      title: m("Repositories"),
      body: m(
        "What this project can read. With none listed, only the analysis of the whole project directory is available.",
      ),
    },
    {
      target: "analysis-run",
      title: m("Running an analysis"),
      body: m(
        "Choose what to look at — the project, code, documents, architecture, git history, work items or test evidence — and, if you like, a repository and a path inside it. The report lists observations and where each was found.",
      ),
    },
    {
      target: "analysis-impact",
      title: m("What a change affects"),
      body: m("Give a commit or a range and see the paths it changed and what they touch."),
    },
    {
      target: "analysis-compare",
      title: m("Comparing references"),
      body: m("Two branches, tags or commits side by side: what each points at, and what differs."),
    },
    {
      target: "analysis-sync",
      title: m("Synchronising"),
      body: m("Records where a repository stands now. Offered to a role that manages sources."),
    },
    WHERE_NEXT,
  ],
  maintenance: [
    {
      target: "maintenance-sweeps",
      title: m("Quality sweeps"),
      body: m("Three checks over the project's records. They report and change nothing."),
    },
    {
      target: "maintenance-index",
      title: m("The search index"),
      body: m(
        "Rebuilds the project's text index from its records, for when search misses something it should find. The index for search by meaning is kept by the embedding worker, not by this.",
      ),
    },
    {
      target: "maintenance-check",
      title: m("Checking text"),
      body: m("Paste text to find secrets in it, or to get it back redacted, before it goes anywhere else."),
    },
    {
      target: "maintenance-export",
      title: m("Export"),
      body: m("Writes a full copy of this project to the server's export volume, for a limited time."),
    },
    WHERE_NEXT,
  ],
  members: [
    {
      target: "members-list",
      title: m("Members"),
      body: m(
        "Every grant in this workspace: the person, the role, where it applies, when it was granted and whether it is still active.",
      ),
    },
    {
      target: "members-actions",
      title: m("Changing somebody's access"),
      body: m(
        "Beside each active grant: change its role, issue a password reset, see what the person downloaded, or revoke the grant. Your own grant offers Revoke only.",
      ),
    },
    {
      target: "members-grant",
      title: m("Another grant"),
      body: m(
        "Gives somebody already in this workspace another role, on the whole workspace or on one project. Nothing is sent to them.",
      ),
    },
    {
      target: "members-invite",
      title: m("Adding somebody"),
      body: m(
        "Creates an account with a first role on the whole workspace and shows its setup token once. The person sets their password with it.",
      ),
    },
    WHERE_NEXT,
  ],
  teams: [
    {
      target: "teams-list",
      title: m("Teams"),
      body: m("Each team with its members. Rename, delete, or open its members to add and remove people."),
    },
    {
      target: "teams-new",
      title: m("A new team"),
      body: m("Creates an empty team. People are added from this workspace's members."),
    },
    WHERE_NEXT,
  ],
  workspaces: [
    {
      target: "workspaces-list",
      title: m("Your workspaces"),
      body: m("Every workspace you can reach, with your role in each."),
    },
    {
      target: "workspaces-new",
      title: m("A new workspace"),
      body: m("Creates a workspace, sponsored by this one, with you as its administrator and, if you name one, a first project."),
    },
    WHERE_NEXT,
  ],
  audit: [
    {
      target: "audit-filters",
      title: m("Choosing what to read"),
      body: m(
        "Choose a project, how many days back, and a channel. The history is read one project at a time.",
      ),
    },
    {
      target: "audit-entries",
      title: m("The entries"),
      body: m(
        "Newest first: when, the action, whether it was allowed, who, on which channel, the resource and a detail of identifiers and counts.",
      ),
    },
    WHERE_NEXT,
  ],
  health: [
    {
      target: "health-components",
      title: m("System health"),
      body: m(
        "Whether each part the server depends on answered just now: the database, the evidence store and the rest. A detail says what failed, never a connection string or a password.",
      ),
    },
    {
      target: "health-backup",
      title: m("Backup"),
      body: m("Writes a backup of every workspace now, beside any the operator schedules."),
    },
    WHERE_NEXT,
  ],
  pluginAccess: [
    {
      target: "plugin-tokens",
      title: m("Your tokens"),
      body: m(
        "The machine tokens you minted in this workspace, when each was last used, and whether it still works. Revoke one you no longer use, or one on a machine you lost.",
      ),
    },
    {
      target: "plugin-mint",
      title: m("Minting a token"),
      body: m(
        "Name it after the machine it will live on and choose how many days it lasts. Copy the value at once: it is shown once.",
      ),
    },
    {
      target: "plugin-connect",
      title: m("Connecting a folder"),
      body: m(
        "The command to run in the folder where you use Claude Code or Codex, with this server and workspace filled in, and one per project to make it the default. Then run devbuddy doctor.",
      ),
    },
    WHERE_NEXT,
  ],
} satisfies Record<string, TourStep[]>;
