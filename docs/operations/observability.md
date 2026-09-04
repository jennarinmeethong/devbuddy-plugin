# Traces, metrics, and logs

`docs/plan.md` named OpenTelemetry in its technology choices and nothing had picked it up. This is
that, plus the backend to send it to: an **optional overlay** on the shipped stack, off unless an
operator turns it on.

It is also the answer to the log-retention question `logging.md` leaves open. That file lists
three options for holding logs the 90 days the retention schedule names; option 2 — an aggregator
that owns a time-based policy — is what this overlay is. Note the limit of that: running this
makes the 90-day window *available*, not automatic. The base stack still rotates by size, and log
export is off until SMTP is configured. What settles the question is an operator choosing one of
the three and recording it.

## Turning it on

Two files instead of one:

```bash
docker compose -f docker/compose.yaml -f docker/compose.observability.yaml up -d
```

That adds five services to the eight-service stack: an OpenTelemetry collector, Prometheus, Loki,
Tempo, and Grafana. Grafana is published on **loopback only** (`127.0.0.1:3000`); nothing else in
the overlay is published at all. Set `DEVBUDDY_GRAFANA_PASSWORD` in `docker/.env` first — it has
no default, because a default administrator password on a dashboard that shows security-control
data is not a convenience.

Without the second `-f`, nothing changes: `Telemetry:Endpoint` is unset, `AddDevBuddyTelemetry`
returns before registering anything, and the application pays nothing for a feature it is not
using.

## What is collected

Three signals, and one of them is off by default.

**Traces.** One span per operation, named for the operation, carrying `devbuddy.operation`,
`devbuddy.channel`, and `devbuddy.outcome`. ASP.NET Core and HttpClient instrumentation sit around
it. Sampled at `Telemetry:TraceSampleRatio` (default `0.1`).

**Metrics.** `devbuddy_operations_total`, `devbuddy_operation_duration`,
`devbuddy_content_blocked_total`, `devbuddy_analysis_duration`, `devbuddy_analysis_rejected_total`,
plus .NET runtime, HttpClient, and — in the API — the rate limiter's own meter.

**Logs are not exported unless `Telemetry:ExportLogs` is set true**, and it defaults to false on
purpose. With `EmailOptions.Provider` on its own default of `Log`, setup and recovery tokens are
written into the application log deliberately, so an operator can complete an account by hand.
Shipping that log to a system more people can read copies single-use credentials into it.
Configure SMTP first, then turn this on; `logging.md` has the settings.

## What is deliberately not collected

The tagging rule, stated once in `DevBuddyTelemetry` and enforced by tests: a tag may carry an
operation name, an outcome, a channel, or a scanner rule name — a fixed vocabulary this codebase
defines. It may never carry a workspace, project, record, or user identifier, a denial reason, or
anything a caller supplied.

That is what keeps this overlay from becoming a way around SB-06 and SB-18. Dashboards answer *how
many* and *how often*; the audit log, which is authorised and tenant-scoped, answers *who* and
*which* — and the trace and the audit event share a correlation id, so an operator who is entitled
to the second can get there from the first.

Three consequences, each a decision rather than an omission:

- **Database instrumentation is absent.** Npgsql and EF Core instrumentation put statement text on
  a span. Everything here is parameterised, so values are not in that text, but a query shape is
  still more than the vocabulary allows. Database time appears inside the operation span's
  duration, which is the question being asked anyway.
- **URL path, query, and full URL are stripped from every span** by `ScrubIdentifiersProcessor`,
  which runs after all other instrumentation so it also sees what a host contributed. The route
  *template* survives — `POST /operations/{name}`, not `/operations/create_team` with a body — so
  spans stay groupable without carrying identifiers.
- **The rate limiter's metrics are dimensioned by policy and route**, never by the partition key.
  The key is an account id or an IP address; the count is what matters.

Verified against the running stack rather than asserted: with a workspace, project, and actor
created and traffic generated through all three outcomes, no series and no span carried any of
those three identifiers, and a blocked secret appeared only as
`devbuddy_content_blocked_total{devbuddy_control="secret",devbuddy_rule="aws-access-key-id"}` —
the rule's name, never the matched value.

## Retention

Set per backend, in the overlay:

| Signal | Backend | Window | Where |
| --- | --- | --- | --- |
| Metrics | Prometheus | 90 days | `--storage.tsdb.retention.time=90d` |
| Logs | Loki | 90 days (`2160h`) | `docker/observability/loki.yaml` |
| Traces | Tempo | 14 days (`336h`) | `docker/observability/tempo.yaml` |

Metrics and logs match the schedule's 90 days. Traces are held for two weeks because they are the
bulkiest signal and the shortest-lived question — a trace answers "what happened in this request",
which is asked within days or not at all. Raise it if that is wrong for your installation; the
storage cost is the reason it is not already higher.

These are the aggregator's own policies, so this is genuine time-based retention rather than the
size-bounded rotation `compose.yaml` gives on its own.

## The dashboard

One is provisioned: **DevBuddy — controls**, in the DevBuddy folder. It is deliberately about the
security controls rather than about throughput, because throughput is what every stack already
shows and controls are what this one is for.

Eight panels: operations by outcome, operations by channel, refusals by operation, content blocked
by control and rule, operation p95, analysis p95 against the concurrency ceiling, analysis
rejections, and rate-limiter rejections.

Two of them are the ones to watch. **Content blocked by control and rule** is SB-17 and SB-18
doing their job — a rising line is the scanner catching things, not a fault. **Refusals by
operation** is authorization; a sustained climb on one operation is either a misconfigured
integration or somebody probing, and the audit log is where you find out which.

Note what "refusals" does and does not include. A request with no bearer token is rejected by
authentication middleware before the pipeline runs, so it is an HTTP 401 in the ASP.NET Core
metrics and *not* a `Denied` operation. `devbuddy_operations_total{devbuddy_outcome="Denied"}`
means an authenticated caller who lacked the permission — which is the more interesting signal of
the two.

## Pointing it somewhere else

Nothing here requires the bundled backends. Set `DEVBUDDY_Telemetry__Endpoint` to any OTLP
endpoint — a hosted vendor, an existing collector — and run `compose.yaml` alone. The application
speaks OTLP over gRPC and knows nothing about what is on the other end.

The tagging rule travels with it. It is enforced in the application, before export, so it holds
against whatever backend receives the data.
