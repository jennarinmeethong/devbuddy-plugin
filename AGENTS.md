# Repository Guidelines

## Project Structure & Module Organization

This repository is currently an empty scaffold. Tracked content consists of
`README.md`, which identifies the project, and this guide. `CLAUDE.md` may be
present locally with tool-specific notes, but it is not tracked. No `src/`,
`tests/`, asset directory, dependency manifest, or generated-output directory
exists yet.

When implementation begins, keep production code under a single clear root
(for example, `src/`), colocate small fixtures with their tests, and reserve
`assets/` for static files. Update this document when the structure becomes
established.

## Build, Test, and Development Commands

There are no build, lint, format, test, or local-run commands configured at
present. Do not claim that commands such as `npm test` or `make build` work
until their configuration files are committed.

For the current scaffold, use these checks:

```powershell
git status --short       # Review local changes
git diff --check         # Detect whitespace errors
git log --oneline -10    # Review recent commit style
```

Add the project’s actual setup, development, validation, and release commands
here as soon as tooling is introduced.

## Coding Style & Naming Conventions

No language-specific formatter or linter has been selected. Follow the
conventions of the chosen ecosystem and commit its formatter/linter
configuration with the first code changes. Prefer descriptive lowercase
directory names (for example, `src/commands`) and names that match the
language’s standard conventions. Avoid unrelated reformatting in functional
changes.

## Testing Guidelines

No test framework or coverage target is configured. New features should include
automated tests when a test runner is added. Name tests for observable behavior,
such as `creates_config_when_missing`, and keep test files in the location
prescribed by the selected framework. Document the exact test command and any
coverage threshold here.

## Commit & Pull Request Guidelines

The history currently contains only `Initial commit`, so no established commit
convention can be inferred. Use concise, imperative subjects such as `Add
plugin manifest` or `Document local setup`; keep each commit focused.

Pull requests should explain the change and validation performed, link related
issues when available, and include screenshots for user-visible behavior.
Mention intentionally deferred work or configuration requirements in the PR
description.
