---
description: Work out what a change affects, against recorded knowledge
argument-hint: [commit, range, or what you are about to change]
---

Work out the impact of `$ARGUMENTS`.

1. `analyze_change_impact` for the commit or range, to find affected modules, APIs, tests, and
   documents.
2. `search_knowledge` for records covering what it touches — a decision that constrains this area
   is exactly what you would otherwise walk into.
3. `find_missing_evidence` if a claim about the change has nothing behind it.

Analysis is read-only. It reads files and git objects as data and runs nothing, so a repository
whose objects are packed will say so rather than reporting empty history — treat that as a real
answer and mention it.

Report affected areas, the recorded decisions that bear on them, and what is not covered. Do not
create a draft unless you were asked to.
