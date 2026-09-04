# Backup and restore

A backup nobody has restored is a belief. This one is exercised by a test that destroys a database
and puts it back — `RestoreDrillTests` — and what follows is the same procedure by hand.

## What a backup is

A directory under `Backup:RootPath`, named for the moment it was taken:

```
backup-20260903-041500-a1b2c3.../
  rows.json          every table, unfiltered
  evidence/          one file per artefact, named by its identifier
```

It is a **logical** backup: rows read through the application, not a `pg_dump`. That is a
deliberate trade. A physical backup means running an external program, and running anything from
product code would put process APIs into an assembly that a build-breaking test scans to keep them
out (SB-04). Reading the rows costs a little performance and keeps that guarantee whole.

**It is not a replacement for a database-level backup at scale.** For a self-hosted installation of
the size v1 targets it is the whole story; for anything larger, take this *and* a `pg_dump` from
outside the containers, on a schedule.

### What is in it

Everything except two things.

**Sessions are left out.** Refresh and recovery tokens are short-lived, and restoring them would
resurrect whatever happened to be signed in at the moment the backup was taken. After a restore,
everyone signs in again. That is the correct outcome, and it is worth telling people before they
report it as a bug.

**Passwords and machine tokens are kept.** People get back in with the password they had, and the
plugin configurations on their laptops keep working. An installation where everybody had to
recover their account after a restore would be a worse outcome than the one being recovered from.

Generated columns are also absent — the full-text search vector is derived from the title and body
and PostgreSQL recomputes it on write. It is an index, not data.

## Taking one

Through the console:

```bash
dotnet run --project src/hosts/DevBuddy.Cli -- backup --workspace <workspace-id> --actor <your-id>
```

Or through the API, as an administrator, with `backup_system`.

The reference it prints is what a restore takes. Keep it.

**Put the backup root somewhere outside the containers.** A backup that lives on the volume it
backs up is not a backup. In the shipped Compose stack it is a named volume (`backups`), which
survives container replacement but not the loss of the host — copy it off the machine.

## Restoring

A restore requires an **empty** installation. Merging a backup into a populated database would
half-succeed on every conflicting identifier and leave a mixture nobody could reason about, so a
restore that finds anything already there refuses.

It also refuses a backup taken at a different schema version. Rows written against one schema and
read into another is the failure that looks like success until somebody opens a record.

The drill, end to end:

```bash
# 1. Stop the servers. Nothing should be writing while this happens.
docker compose -f docker/compose.yaml stop api mcp

# 2. Destroy the database volume. This is the disaster, performed on purpose.
docker compose -f docker/compose.yaml down
docker volume rm devbuddy_database

# 3. Bring the database back empty and migrate it.
docker compose -f docker/compose.yaml up -d database
docker compose -f docker/compose.yaml run --rm migrate

# 4. Restore. A console command, not an operation: an empty database holds no membership
#    to authorise a caller against, so there is no actor to pass.
docker compose -f docker/compose.yaml run --rm migrate restore --reference <reference>

# 5. Start everything and check.
docker compose -f docker/compose.yaml up -d
```

Then confirm, and confirm the part people forget: open a record that has evidence attached and
download the attachment. A restore that returned the rows and left the bytes behind looks like a
success right up until a reader clicks something.

## What to check after a restore

| | How |
|---|---|
| Records | The review queue and a published record, in the web UI. |
| Approvals | A published record still shows the approval and the hash it was bound to. |
| Evidence | Download an artefact. Rows without bytes is the failure mode this catches. |
| Audit history | A system recovered without its history cannot say who approved what. |
| Accounts | Sign in. Everybody will have to; sessions are not restored. |
| Plugins | A machine token that worked before still works. |

## Retention

Backups carry the retention schedule too — a backup is a complete copy of everything, including
whatever was deleted from the primary store since (ADR-0009, SB-27). `Backup:Retention` records how
long they are meant to be kept, and a backup past that window is deleted the next time

```bash
dotnet run --project src/hosts/DevBuddy.Cli -- retention
```

runs. That command also purges audit events past their window, evidence no revision references
past its grace period, and exports past theirs — every copy this build makes a durable one of. It
runs nothing on its own; put it on whatever cron or task scheduler the deployment already has, on
whatever cadence fits. Application log retention is the one copy this does not reach — a container
log-driver setting, not application code — see `docs/security/release-readiness.md`.
