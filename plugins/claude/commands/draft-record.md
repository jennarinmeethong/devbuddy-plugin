---
description: Write down what was learned, as a draft awaiting human approval
argument-hint: [what should be recorded]
---

Draft a knowledge record for `$ARGUMENTS`.

Before writing anything, `search_knowledge` for what is already recorded. A near-duplicate is
worse than nothing: it splits the answer in two and neither half is obviously the current one.

Then `get_work_item` for the work this belongs to — a record hangs off a work item, and you need
its identifier. Choose the record kind deliberately: a decision with its rationale and the
alternatives considered is a `Decision`; what a change affected is a `ChangeImpact`; what the next
person needs is a `Handover`.

Give it real provenance: where this came from, who worked it out, and when. It is required, and it
is what makes the record worth trusting later.

Then `create_draft`.

Say clearly afterwards that it is a **draft awaiting approval**, not a published record, and that a
person has to review the exact revision before anyone reading published knowledge will see it. Do
not paste configuration, logs, or environment blocks into the body — the server refuses content
carrying a credential rather than storing it redacted.
