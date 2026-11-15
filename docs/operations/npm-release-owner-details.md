# npm release owner details — 2026-10-25

Source: owner's filled preparation template. This records inputs without granting release approval.

| Item | Owner's response / status |
|---|---|
| Code owner | `jennarin.meethong` |
| Public disclosure permission | Owner chose internal use first on 2026-10-28; public disclosure/public npm publication not authorised |
| Internal distribution | Owner deferred repository distribution and requested temporary private staging on current DevBuddy server (2026-10-28); employee download/rollout and public publication not enabled |
| Staged candidate | Tested `1.14.0-next.0` tarball, checksum and guide at `devbuddy:/home/jm/devbuddy-internal-packages/1.14.0-next.0-99f0c5ea9e92/`; owner-only 0700/0600, transferred checksum verified; candidate version not yet release-approved |
| Dependency installation | Owner selected A, online npm dependency downloads through organisation-approved access (2026-10-28); no offline bundle or new private registry; corporate network reachability remains unverified |
| Selected internal release repository | Owner selected `jennarinmeethong/devbuddy-plugin`; GitHub metadata on 2026-10-28 reports public visibility. Incompatible with internal-only scope until owner resolves destination/visibility. No upload or visibility change performed. |
| Internal audience | All personnel within G-Able, as specified by owner on 2026-10-28; external/public distribution not authorised; corporate approval not independently verified |
| License | Owner accepted internal terms draft content on 2026-10-28 for preparing the pilot edition after checklist closure; not final rollout/public license approval. Draft remains outside tested tarball |
| Intended copyright owner | `jennarin.meethong` |
| npm account | Owner reports existing username `jennarin.meethong`; not independently verified |
| First publisher | `jennarin.meethong` |
| Preferred package | `devbuddy-plugin-client`; rights/availability not verified |
| Executable | `devbuddy-npx`, unchanged |
| Confirmed fallback | `@jennarin.meethong/devbuddy-plugin-client`; spelling corrected by owner on 2026-10-25; publishing rights not verified |
| Publishing repository | `https://github.com/jennarinmeethong/devbuddy-plugin` |
| Trusted-publisher administrator | Undecided |
| Organization release/environment requirements | Undecided |
| Pilot version | Owner selected `1.14.0-next.0` on 2026-10-28; retain tested `99f0c5ea...` artifact; employee rollout approval remains pending |
| Prerelease `next` approval | Undecided; not authorised |
| Release approver/date | Owner designated `jennarin.meethong` on 2026-10-28; final internal rollout decision/date still pending |
| Pilot support | Direct reports to owner via Microsoft Teams display name `jannarin meethong`, supplied on 2026-10-28; account not independently verified; approver remains `jennarin.meethong` |
| Plugin default scope | Owner selected npm default for the G-Able internal pilot kit only, 2026-10-28; original repository plugins/existing host settings unchanged; kit preparation/testing authorised, final rollout pending |

The package remains private. Do not infer npm ownership from an unused-looking name, or infer
public disclosure/license approval from the presence of a GitHub URL. No credentials are needed
in this document. Continue implementation and isolated testing independently of these decisions.

## License options for the owner's review

| Option | Main effect |
|---|---|
| [MIT](https://choosealicense.com/licenses/mit/) | Allows commercial use, modification and redistribution, including closed-source derivatives; copyright/license notices must remain. |
| [Apache-2.0](https://choosealicense.com/licenses/apache-2.0/) | Permissive like MIT, with an express contributor patent grant and additional notice/change requirements. |
| [GPL-3.0](https://choosealicense.com/licenses/gpl-3.0/) | When distributing covered modified works, requires corresponding source and the same license; commercial use is allowed. |

These are summaries for choosing what to review, not a license selection. First establish the
right to disclose the code, then approve a license consistent with that right and dependency
obligations. No LICENSE file or license metadata has been added.

An npm scope follows the actual user/organization name:
[npm scopes](https://docs.npmjs.com/about-scopes/). Fallback spelling is confirmed; verify publishing rights before use.
Trusted publishing links a specific CI workflow to package publication through OIDC;
the npm package administrator configures it after the release workflow is ready:
[npm trusted publishing](https://docs.npmjs.com/trusted-publishers/).
