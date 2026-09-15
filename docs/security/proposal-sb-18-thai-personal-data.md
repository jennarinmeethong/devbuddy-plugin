# Proposal: Thai personal data in the SB-18 rule set

**Status: proposed, awaiting the project owner's decision.** Nothing here is confirmed. `info.md`
records no decision about it, and the SB-18 row in `verification-matrix.md` is unchanged. If the
owner approves all or part of this, the owner records the decision in `info.md`, and the matrix row
gains the new evidence only once the tests below pass on the approved version.

Branch: `claude/sb-18-thai-personal-data`. Written 2026-09-15 against `v1.2.1`.

## What was observed

On 2026-09-15 a project with AI access enabled and **no bounded data scope** accepted a draft over
the MCP AI channel containing three invented values: an email address, a Thai mobile number and a
Thai national ID. `get_record` returned all three unredacted.

The pipeline behaved as designed. `UseCaseExecutor` scans personal data on the AI channel when no
bounded scope is approved (step 5) and redacts on the way out (step 7). The rule set simply had
nothing that matched. `PersonalDataRules` held three rules: `us-ssn`, `payment-card-number` (Luhn)
and `labelled-personal-data` (English labels only). The national ID is thirteen digits, so it was a
card-number candidate, but it failed Luhn and was dropped.

## Read this first: the exact reproduction still passes

**The proposal as written would not catch two of the three observed values.**

- **The ID fails its own checksum.** `1-1037-01234-56-7` has check digit 7, and the mod-11 rule
  requires 3. A rule gated on the checksum, as asked for, does not report it. It is caught only
  when labelled, for example `เลขบัตรประชาชน: 1-1037-01234-56-7`. That is the same trade the card
  rule makes with Luhn: an invented number is not a real person's number.
- **The address is at a reserved domain.** `somchai.testperson@example.com` is at `example.com`,
  which RFC 2606 reserves, and the proposed email rule exempts reserved domains.

The mobile number `081-234-5678` is caught. The real-pipeline tests use a checksum-valid ID
(`1-1037-01234-56-3`) and a non-reserved domain, and are blocked and redacted as expected. To
re-run the manual reproduction, use those values.

## What would be caught

| Rule | Shape | Accepted when | Not matched |
|---|---|---|---|
| `thai-national-id` (new) | 13 digits grouped 1-4-5-2-1: dashed, spaced or bare, one separator throughout, Thai digits (๐–๙) included | the mod-11 check digit holds | a longer digit run; a wrong check digit |
| `thai-mobile-number` (new) | `081-234-5678`, `081 234 5678`, `08-1234-5678`, `081-2345678`, `0812345678`, `+66 81 234 5678`, `+66812345678`, `+66 081…` — prefixes 06, 08, 09 | always (there is no checksum) | fixed lines (02, 03x, 04x, 05x, 07x) unless labelled; `66…` without `+` |
| `email-address` (new) | `local@domain.tld`, including one joined straight onto Thai text | domain is not RFC 2606 reserved | `git@github.com:org/repo`, `https://user@host/…`, `logo@2x.png`, `react@18.3.1`, `@org/team` |
| `labelled-personal-data` (extended) | Thai labels: บัตรประชาชน / ประจำตัวประชาชน (any compound, e.g. เลขบัตรประชาชน), วันเกิด, วัน เดือน ปีเกิด, หนังสือเดินทาง, พาสปอร์ต, เบอร์โทร, เบอร์โทรศัพท์, เบอร์มือถือ, โทรศัพท์(มือถือ). English: national ID (number), citizen ID (number) | followed by `:` or `=` and a value | a label with no `:`/`=` (common in Thai prose) |

Two further changes come with the labels:

- **A day, month name and year is taken whole as a value.** `12 เมษายน 2533`, `12 เม.ย. 2533` and
  `12 April 1990` are redacted whole. Before, only the day was taken, which also affected English.
- **Checksums normalise Unicode digits.** `\d` in .NET matches Thai digits, so a Thai-digit card
  number is now checked by Luhn too, where before it was rejected by a comparison that assumed ASCII
  digits.

## What stops being stored or served on the AI channel

This applies only where SB-18 applies: **the AI channel, on a project with no approved bounded
scope.** A person on the human channel is unaffected, and so is an AI caller inside an approved
scope.

- **Refused at retention.** `create_draft`, and every other scannable request on that channel, is
  Blocked with nothing stored when its text holds any of the shapes above. The finding names the
  rule and line, never the value.
- **Redacted at egress.** Every redactable response on that channel has the value replaced with
  `[REDACTED]`: `get_record`, search results, handovers, analysis excerpts and the rest. A labelled
  field keeps its label.
- **Embeddings.** `record-embedding-sweep` reads through the same pipeline on the AI channel, so
  newly embedded text is redacted the same way. Records already indexed are **not** re-embedded
  when the rules change, because the sweep keys on the published revision's content hash, and that
  hash does not change. Their vectors were computed from the unredacted text until the record is
  revised. The index holds no text.
- **Git history is unaffected.** `GitObjectStore` keeps a commit author's name and drops the email,
  and the GitHub API client reads login names, so emails reach the AI channel only through record
  text or analysed file contents.

## False-positive risk, rule by rule

A false positive on the retention side is a **refused draft**: the assistant has to reword or leave
the value out. On the read side it is a `[REDACTED]` where ordinary text was.

### `thai-national-id`: low, but not zero

- **Why it stays low:** a bare 13-digit run passes the check digit one time in ten, the same odds
  as the card rule under Luhn.
- **The engineering-text case:** a **millisecond Unix timestamp** is 13 digits.
  `1757894400001` (2025-09-15T00:00:00.001Z) passes, and a test pins that as a known cost. Roughly
  one bare millisecond timestamp in ten in AI-channel text would be refused or redacted.
- **Alternatives for the owner:**
  1. **As implemented:** checksum required, with or without separators.
  2. **Require the 1-4-5-2-1 separators, or a label, for a bare run.** This removes the timestamp
     case and still catches the dashed form people actually write. It misses a bare real ID written
     without a label.
  3. **Accept the dashed 1-4-5-2-1 grouping even with a wrong check digit.** This catches invented
     examples like the observed one. That grouping rarely appears in engineering text, but
     invented example IDs would then block drafts too.

### `thai-mobile-number`: moderate, with no checksum

- **What it rests on:** the leading `0[689]` and the refusal to match inside a longer run.
- **Where it can still fire:** a ten-digit zero-padded identifier starting 06, 08 or 09, such as an
  order or ticket number written `0912345678`. UUID segments, `HH:MM:SS` times, dotted versions and
  10-digit Unix timestamps (which start with 1) are tested and not matched.
- **Alternatives for the owner:**
  1. Include as implemented.
  2. Require a separator or `+66`, dropping the bare ten-digit form.
  3. Leave phones to labels only.
- **Fixed lines:** not matched unless labelled. They are mostly business numbers, and nine digits
  collide with much more text.
- **Label asymmetry:** English phone labels (`phone:`, `mobile:`) are not added here, although Thai
  ones are.

### `email-address`: the highest-volume rule

Emails are routine in engineering text: `Co-Authored-By:` and `Signed-off-by:` trailers, `AUTHORS`,
`package.json` `author` fields, CODEOWNERS entries that name an address rather than `@team`,
service accounts (`…@…iam.gserviceaccount.com`), bot addresses (`noreply@github.com`,
`…@users.noreply.github.com`) and on-call aliases. On a project with no bounded scope, an AI draft
quoting any of these would be **refused**, and a read would show `[REDACTED]`. Many of these
addresses are not personal data (role addresses, bots), and the rule cannot tell.

- **Choices for the owner:**
  1. **Include, with the RFC 2606 exemption (as implemented).** Covered: `example.com`, `.net` and
     `.org` with their subdomains, plus `.example`, `.test`, `.invalid` and `.localhost`. No mailbox
     can exist there, so the exemption cannot hide a real person's address. Its cost is that invented
     examples at other domains are still caught. **`.local` is deliberately not exempt:** Active
     Directory domains are often named that way, and those addresses are employees'.
  2. **Include with no exemption.** Placeholders in docs and test data block drafts too. A one-line
     change: remove the check from the rule.
  3. **Include with a wider exemption,** such as `noreply`, `users.noreply.github.com` and
     service-account domains. Fewer refusals, but `users.noreply.github.com` addresses carry a
     GitHub username, which is pseudonymous personal data.
  4. **Leave emails out of SB-18** and rely on labels and bounded scopes.
- **Not caught either way:** internationalised addresses (non-ASCII local parts or domains).

### Thai labels: low

- **What limits it:** a label needs `:` or `=` and a value.
- **What it misses:** Thai prose often writes `วันเกิด 12 เมษายน 2533` with no colon, and that is
  not caught. The bare-number rules cover IDs and mobiles written that way. A birth date without a
  colon is not covered.
- **A Thai-specific cost:** there is no leading word boundary, so a label joined to the previous
  word still matches, which is what Thai spelling needs. A sentence like `บัตรประชาชน: ไม่ต้องใช้`
  ("ID card: not needed") has its value redacted, and a draft carrying it is refused, as
  `passport: required` already is in English.

## What the owner is asked to decide

1. **Thai national ID:** approve as implemented, or choose alternative 2 or 3.
2. **Thai mobile numbers:** include as implemented, narrow, or leave to labels.
3. **Email addresses:** include with the RFC 2606 exemption, include with none, include with a wider
   one, or leave out.
4. **Thai and English ID labels, and whole date values:** approve or not.
5. **Whether the AL-2 wording in `info.md` needs updating:** "catches known shapes" still describes
   this.

Each rule is independent. Removing one is deleting its line from `PersonalDataRules.All` and its
corpus rows.

## Verification

Run on 2026-09-15 on the owner's Linux test machine, through `dotnetd` (the .NET SDK container),
against a separate clone of this branch. `/data/devbuddy`, the checkout behind the running stack,
was not touched.

**At `a88044b`: 766 tests passed, 0 failed.** `dotnet format --verify-no-changes` was clean.

| Project | Passed |
|---|---|
| DevBuddy.Infrastructure.Tests | 303, including 123 in `PersonalDataCorpusTests` |
| DevBuddy.Application.Tests | 214 |
| DevBuddy.Security.Tests | 115, including the two new real-pipeline tests below |
| DevBuddy.Domain.Tests | 54 |
| DevBuddy.Api.Tests | 49 |
| DevBuddy.McpServer.Tests | 31 |

New in `PersonalDataChannelTests`, over real PostgreSQL through the real pipeline:

- **`the_ai_channel_is_refused_thai_personal_data_when_no_bounded_scope_is_approved`.** A draft
  holding a Thai ID, a mobile number and an email address is Blocked, with nothing stored. It names
  all three rules.
- **`a_read_over_the_ai_channel_redacts_thai_personal_data_when_no_bounded_scope_is_approved`.**
  `get_record` returns `ลูกค้า [REDACTED] โทร [REDACTED] อีเมล [REDACTED]…`.

### Mutation checks

Each guard was broken on purpose, the corpus tests were run, and the change was reverted.

| Mutation | Result |
|---|---|
| Accept any 13-digit grouping without the mod-11 check | 3 failed: both 13-digit negatives, and the observed invented ID |
| Drop the RFC 2606 exemption | 2 failed |
| Remove the `@2x` asset guard | 1 failed |
| Move `email-address` after `thai-mobile-number` | **First run: 0 failed.** The corpus row asserted only that the address no longer appeared whole, and the domain was being released. A test comparing the exact output was added (`a88044b`), and the mutation then failed 1 |

### Not verified

- **The manual MCP reproduction was not re-run** against a built image of this branch. The
  real-pipeline tests cover the same path.
- **The embedding sweep** was not run against the new rules.
- **No measurement of false positives on real engineering text** was made. The false-positive
  cases above come from the corpus, not from a sample of this installation's records.
