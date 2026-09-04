# Application logs and their retention

The one row of the retention schedule (`docs/plan.md`, Phase 11) this codebase does not enforce
for itself. Everything else — audit events, evidence, backups, exports — is purged by
`dotnet run -- retention`, tested, and recorded as `TESTED` against SB-27. Application logs are
not, and this file is what an operator does instead.

## What the application actually does

DevBuddy writes to standard output through the default `Microsoft.Extensions.Logging` console
provider. It does not open a log file, does not rotate anything, and does not delete anything.
That is deliberate: the shipped images are chiseled, with no shell and no writable root
filesystem, and a container that manages its own log files on a volume is a container that has
opinions about disk it cannot see.

So retention is a property of whatever collects the container's output. In the shipped stack that
is the Docker log driver, configured in `docker/compose.yaml`:

```yaml
x-logging: &app-logging
  driver: json-file
  options:
    max-size: "10m"
    max-file: "10"
```

Roughly 100 MB per service, rotated oldest-first.

## Why that is not the 90 days the schedule names

`json-file` rotates on **size**. The schedule says **90 days**. Those coincide only by accident:
the same 100 MB is three years of a quiet installation and four days of a busy one. Nothing here
is broken — the bound is real and the disk cannot fill — but if somebody asks "can you produce
the logs from 60 days ago", the honest answer is "only if the volume happened to work out".

Pick one of the three below and record which, so the next person reads a decision instead of a
default.

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

## Option 2 — A log aggregator with time-based retention — **this one now ships**

The only option that actually implements "90 days", and since the OpenTelemetry work it is no
longer something to assemble yourself. `docker/compose.observability.yaml` is an optional overlay
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

## Option 3 — Make the application own it

Add Serilog with a rolling file sink and `retainedFileTimeLimit: TimeSpan.FromDays(90)`, writing
into a named volume. It would be testable in this codebase, which is the only way SB-27's log row
ever moves from "operator responsibility" to `TESTED`.

It is listed third because it argues with the design: it puts file handling back inside a chiseled
image, adds a dependency, and gives the application an opinion about disk. Worth it under a
compliance requirement that names an application-enforced window. Not worth it to tidy up a table.

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

Whichever option is chosen, note it in `docs/security/release-readiness.md` under "Before real
project data is connected". That section exists so the residual risks are accepted explicitly
rather than discovered later.

The item stays on that list even now that option 2 ships, and the distinction is worth keeping
straight: the overlay makes a 90-day window *available*, it does not make it *true* by default.
Running the base stack alone still gives size-bounded rotation. What closes this is an operator
choosing, and recording, which of the three is in force.
