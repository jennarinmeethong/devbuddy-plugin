# devbuddy-root

A template for the folder that holds your checkouts, when you work against more than one DevBuddy
deployment — or more than one workspace in one deployment — from one machine. A machine token is
bound to one workspace, so that pair is what a root is for.

Copy `.devbuddy/` and `gitignore` (as `.gitignore`) into that folder. `.devbuddy/README.md` travels
with the copy and is what somebody reads once it is in place.

`docs/operations/workspace-layout.md` is the full account: why the settings sit beside the
checkouts rather than inside one, why analysis needs a junction tree, and what this arrangement
does and does not protect.

Nothing in the product reads these files. They arrange the environment the plugin is launched
from; the server still decides everything else, per project, per person, on every call.
