# Application logs and their retention

The row of the retention schedule (`docs/plan.md`, Phase 11) that used to sit outside this
codebase. Everything else — audit events, evidence, backups, exports — is purged by
`dotnet run -- retention`, tested, and recorded as `TESTED` against SB-27. Application logs now
are too, by the same command, when the application is writing its own files.

This file is what an operator decides, because it is still a decision: the shipped stack turns
file logging on, and an installation that would rather keep logs in its own aggregator turns it
off and holds the window there instead.

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

## Option 2 — A log aggregator with time-based retention — **available, not the default**

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

**Log export is off by default even with the overlay running.** `Telemetry:ExportLogs` defaults to
false, and the reason is the first item in the next section: with no SMTP configured, setup and
recovery tokens are written into the application log on purpose. Shipping that log to a system
more people can read copies single-use credentials into it. Configure SMTP first, then set
`DEVBUDDY_TELEMETRY_EXPORT_LOGS=true`.

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

## Option 3 — The application owns it — **this is what ships**

`Logging:File:Path` turns on a Serilog file sink that rolls daily, and
`Logging:File:RetentionDays` (90) is the window. `docker/compose.yaml` sets both and mounts a
named `logs` volume for them.

```yaml
# docker/.env
DEVBUDDY_LOG_PATH=/srv/logs/devbuddy.log
DEVBUDDY_LOG_RETENTION_DAYS=90
```

The window is enforced twice, deliberately. Serilog drops files past the limit as it rolls, which
covers a service that keeps running. `dotnet run -- retention` sweeps the same directory, which
covers one that has been stopped for a month — and it is the half a test can drive without waiting
a day for a roll, so this is the mechanism that moves the schedule's log row from "operator
responsibility" to a passing test
(`RetentionEnforcementTests.a_rolled_log_file_past_its_window_is_deleted_and_a_recent_one_survives`).

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

**Setup and recovery tokens are in there when SMTP is not configured.** `EmailOptions.Provider`
defaults to `Log`, and `LogEmailSender` writes the whole message — address, subject, and the token
in the body — on purpose, so a self-hosted operator can complete an account setup or a password
recovery by hand. It is a working delivery channel, and it means the log holds single-use
credentials. Either restrict who can read logs accordingly, or configure SMTP and stop generating
them:

```bash
# docker/.env
DEVBUDDY_SMTP_HOST=smtp.example.com
DEVBUDDY_SMTP_PORT=587
DEVBUDDY_SMTP_USERNAME=devbuddy
DEVBUDDY_SMTP_PASSWORD=...
DEVBUDDY_SMTP_FROM=devbuddy@example.com
```

Setting a host switches the provider to `Smtp` on its own; nothing else needs changing.

**Redaction (SB-17, SB-18) does not apply to arbitrary log lines.** The secret scanner and the
personal-data redactor run in the use-case pipeline — on content being stored and on responses
leaving the boundary. A stack trace or a framework log line is neither. Treat application logs as
a place where sensitive material can appear, not as a place it has been filtered out of.

## Recording the decision

Whichever option is in force, note it in `docs/security/release-readiness.md` under "Before real
project data is connected". That section exists so residual risks are accepted explicitly rather
than discovered later.

The shipped default is option 3, and that is a real answer rather than a deferral — but it is
still worth writing down, because the two things an operator can silently change are the path
(clearing it reverts to option 1) and whether anything runs `retention` on a schedule. A window
nothing sweeps is a window in name only.
