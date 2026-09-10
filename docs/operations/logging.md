# Application logs and their retention

The row of the retention schedule (`docs/plan.md`, Phase 11) that used to sit outside this
codebase. Everything else — audit events, evidence, backups, exports — is purged by
`dotnet run -- retention`, tested, and recorded as `TESTED` against SB-27. Application logs now
are too, by the same command, when the application is writing its own files.

This file is what an operator decides, and for this deployment it is decided: **option 2 is in
force**, confirmed by the project owner on 2026-09-10 (`info.md`) as the acceptance Phase 12A
asked for. Logs go to the aggregator in `docker/compose.observability.yaml` and Loki's ninety days
is the retention of record; the application's own file sink stays on as the local copy with its own
sweep, so the window is enforced twice. Options 1 and 3 remain documented because they remain
legitimate for a different installation, and option 3 is still what the base stack ships on its
own.

Two things changed under this file since v1, and both narrow what it used to hand to the operator:

- **The sweep is scheduled by the stack.** `docker/compose.yaml` runs `retention --every 24h` in
  the console image. "Whether anything runs `retention` on a schedule" is no longer a question an
  operator has to answer, and a window nothing sweeps is no longer the default state.
- **Tokens are not written to logs unless somebody asks.** `Email:AllowTokensInLog` is false. The
  section below on what is in the logs is much shorter than it was for that reason.

## What the application actually does

DevBuddy writes to standard output through the default `Microsoft.Extensions.Logging` console
provider, and — when `Logging:File:Path` is set — to a daily file that it rolls and sweeps itself.

`docker/compose.yaml` sets that path, so the shipped stack gets the file. A bare `docker run` of
one of these images does not: the containers run with a read-only root filesystem, and a default
that wrote files would make every such run fail at startup rather than log to the console as
before. Off unless asked for is the same posture telemetry and SMTP take here.

The container's own output is still collected by the Docker log driver, configured in
`docker/compose.yaml`:

```yaml
x-logging: &app-logging
  driver: json-file
  options:
    max-size: "10m"
    max-file: "10"
```

Roughly 100 MB per service, rotated oldest-first. That bound has not gone away and does not need
to: it now covers the copy on stdout, while the file is the copy with a time-based window.

## Why a size bound is not the 90 days the schedule names

`json-file` rotates on **size**. The schedule says **90 days**. Those coincide only by accident:
the same 100 MB is three years of a quiet installation and four days of a busy one. Nothing about
it is broken — the bound is real and the disk cannot fill — but if somebody asks "can you produce
the logs from 60 days ago", the honest answer is "only if the volume happened to work out".

That is the gap the file sink closes, and it is why option 3 below is the one that ships. The
other two are still here because they are still legitimate choices, and because an installation
that already runs a log platform should use it rather than this.

Pick one and record which, so the next person reads a decision instead of a default.

## Option 1 — Size-bounded, sized deliberately

Fine for a single-operator installation. It stops being guesswork the moment you measure.

Measure a week of real traffic first:

```bash
docker inspect --format='{{.LogPath}}' devbuddy-api-1
```

Then look at the file that names, and at its rotated siblings:

```bash
sudo ls -lh /var/lib/docker/containers/<id>/
```

Divide by the days the stack has been up. Multiply by 90. Set `max-size` × `max-file` to at least
that, in `docker/compose.yaml`:

```yaml
x-logging: &app-logging
  driver: json-file
  options:
    max-size: "20m"     # 20 MB per file
    max-file: "10"      # ten of them: 200 MB total
```

Re-measure when traffic changes materially. This is the option that stays a guess if nobody ever
looks at it again.

## Option 2 — A log aggregator with time-based retention — **this is the option in force**

Also implements "90 days", and since the OpenTelemetry work it is no longer something to assemble
yourself. Choose it over option 3 when logs should live where an organisation's other logs live —
searchable next to traces and metrics, and readable without shell access to a volume. `docker/compose.observability.yaml` is an optional overlay
carrying a collector, Prometheus, Loki, Tempo, and Grafana, with Loki's retention set to the 90
days the schedule names:

```bash
docker compose -f docker/compose.yaml -f docker/compose.observability.yaml up -d
```

See `docs/operations/observability.md` for what it collects, what it deliberately does not, and
the retention window per signal.

Two things to know before choosing it for logs specifically.

**Log export is on with the overlay, and it was not always.** `Telemetry:ExportLogs` still
defaults to false in the application — shipping logs out of the boundary is a third egress beside
the AI channel and the trace exporter, and one an installation crosses on purpose — but
`compose.observability.yaml` sets it true, because option 2 is the option in force here.

What used to keep it false was sharper than caution: with no SMTP configured, setup and recovery
tokens were written into the application log unconditionally, so shipping those logs to a system
more people can read copied single-use credentials into it. That is closed at the source now
(`Email:AllowTokensInLog`, below). An operator who turns that opt-in back on should set
`DEVBUDDY_TELEMETRY_EXPORT_LOGS=false` with it, or accept that the tokens travel with the logs.

**This routes logs through the application, not through the Docker log driver.** The alternative —
the `loki` log driver — is still available and needs no application change:

```yaml
x-logging: &app-logging
  driver: loki
  options:
    loki-url: "http://loki:3100/loki/api/v1/push"
    loki-retries: "2"
    loki-max-backoff: "1s"
```

It is a plugin (`docker plugin install grafana/loki-docker-driver:latest --alias loki
--grant-all-permissions`), so it is one more thing to install and keep working, and it captures
container stdout wholesale — including any log line written before the application's own logging
is configured. Prefer it if you want retention to cover crash output and startup failures too;
prefer `ExportLogs` if you want the same resource attributes on logs as on traces and metrics.

## Option 3 — The application owns it — **what the base stack ships, and the local copy here**

`Logging:File:Path` turns on a Serilog file sink that rolls daily, and
`Logging:File:RetentionDays` (90) is the window. `docker/compose.yaml` sets both and mounts a
named `logs` volume for them.

```yaml
# docker/.env
DEVBUDDY_LOG_PATH=/srv/logs/devbuddy.log
DEVBUDDY_LOG_RETENTION_DAYS=90
```

The window is enforced twice, deliberately. Serilog drops files past the limit when it opens its
own, and `dotnet run -- retention` sweeps the same directory.

**Expect the sweep to report zero, and do not read that as broken.** In the shipped stack the
`retention` command runs in a container that is itself configured to write to that directory, so
its own logger cleans up on startup a moment before the sweep looks — observed doing exactly that
when this was run against the stack. The sweep is what covers a directory nothing is currently
writing to, and it is the half a test can drive without waiting a day for a roll, which is what
moves the schedule's log row from "operator responsibility" to a passing test
(`RetentionEnforcementTests.a_rolled_log_file_past_its_window_is_deleted_and_a_recent_one_survives`,
which runs without a Serilog sink present so only the sweep can be doing the work).

The sweep reads the date out of the file name, which Serilog writes as the roll date, rather than
from a filesystem timestamp: copying a directory resets those, and a restored backup would
otherwise look like a fresh set of logs. Every file the sink creates carries a date, today's
included — today's is simply never ninety days old — so anything in that directory *without* one
was put there by something else and is left alone.

**What it costs**, stated plainly because this file used to list it third for these reasons: a
dependency, and file handling inside an image built not to need any. The read-only root filesystem
is why the volume mount is not optional, and why a misconfigured path fails at startup rather than
quietly falling back to console only — an operator who asked for files and silently got none would
find out when they went looking for a log that was never written.

Turning it off is clearing the path. Everything reverts to standard output and the log driver.

## What is in the logs, which matters more than how long they are kept

Two things to know before choosing where these end up.

**Setup and recovery tokens are not in there — unless you asked for that.** Through v1 they were,
unconditionally: `EmailOptions.Provider` defaults to `Log`, and `LogEmailSender` wrote the whole
message, token included, so a self-hosted operator could complete an account setup or a password
recovery by hand. It was a working delivery channel and it meant every log held single-use
credentials, which `docs/security/release-readiness.md` carried as an accepted risk rather than as
a control.

`Email:AllowTokensInLog` is false by default now. Left alone, the fallback sender records that a
message could not be delivered, who it was for, and how to fix that, and writes the token nowhere.
Two ways to make one reachable, and they are not equivalent.

Deliver it properly:

```bash
# docker/.env
DEVBUDDY_EMAIL_PROVIDER=Smtp
DEVBUDDY_SMTP_HOST=smtp.example.com
DEVBUDDY_SMTP_PORT=587
DEVBUDDY_SMTP_USERNAME=devbuddy
DEVBUDDY_SMTP_PASSWORD=...
DEVBUDDY_SMTP_FROM=devbuddy@example.com
```

Both lines are read: the provider is **not** inferred from a host being set. An earlier version of
this file said it was, and `docker/compose.yaml` says why it is not — Compose can substitute one
value when a variable is present, but the else branch is the empty string, and an empty string is
not a member of that enum, so a deployment that left SMTP alone would fail to start on a setting
it never touched.

Or accept the log, knowingly:

```bash
# docker/.env
DEVBUDDY_EMAIL_ALLOW_TOKENS_IN_LOG=true
```

That restores exactly the v1 behaviour, and anything that can read the log volume — or the
aggregator the logs are shipped to — can use the tokens in it. Turn log export off if you do this.

What works with neither set: **inviting somebody**, because `create_user_account` returns the
setup token in its own response, which was always a real delivery path rather than a fallback one.
What does not: **self-service password recovery**. The token is generated, is unreachable, and
expires. That is the intended consequence of making this a decision.

**Redaction (SB-17, SB-18) does not apply to arbitrary log lines.** The secret scanner and the
personal-data redactor run in the use-case pipeline — on content being stored and on responses
leaving the boundary. A stack trace or a framework log line is neither. Treat application logs as
a place where sensitive material can appear, not as a place it has been filtered out of.

## The decision on record

**Option 2, confirmed by the project owner on 2026-09-10.** The entry is in `info.md` and what it
accepted is written up in `docs/security/release-readiness.md`. That section exists so residual
risks are accepted explicitly rather than discovered later, and until the entry existed the gate
on connecting real project data was open in fact and closed only on paper.

One thing an operator can still silently change: clearing `Logging:File:Path` drops the local
file copy. Under option 2 that costs less than it used to, because Loki holds the time-based
window either way — but it does mean losing the copy you can read from the volume when the
aggregator itself is what is broken.

If you are running a different installation and choose a different option, record which. A window
nothing sweeps is a window in name only, and the difference between this file and a guess is that
somebody wrote down which one is true.
