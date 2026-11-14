# Kentico to Umbraco Migrator Constitution

## Core Principles

### I. Client Data Stays Private (NON-NEGOTIABLE)

- `Sources/`, `sources/`, `_work/` and `runs/` MUST be git-ignored before anything is committed.
- Nothing from the client material MAY enter the repository, README, tests, fixtures, logs that
  are committed, or commit messages: no data rows, listing or brand names, user records, code,
  templates or rich text.
- Only anonymised aggregate numbers (counts, sizes, timings) and section-level path names MAY
  appear. The site name (PontoonsOnly) is allowed.
- Tests and the public sample dataset MUST use invented content only.

Rationale: the source site is client property and the repository is public.

### II. Read-Only Source, API-Only Target

- Kentico is read with direct SQL only. The tool MUST NOT write to the Kentico database or modify
  any file under `Sources/`.
- Umbraco is written only through its Management API (and the one migration endpoint the host
  site adds for redirects). The tool MUST NOT write to the Umbraco database directly.
- All Kentico-version-specific SQL MUST live behind `IKenticoReader`, so another reader (for
  example Kentico Xperience 13) can be added without touching the rest.

Rationale: the brief requires official APIs on the target and a replaceable source reader.

### III. Idempotent, Verifiable Runs

- Every migrated item MUST be keyed by a stable identifier derived from Kentico (NodeGUID,
  AttachmentGUID, Media FileGUID, or a UUID v5 of a natural key). A second run MUST create no
  duplicates and MUST NOT rewrite unchanged items.
- Before each run the tool MUST snapshot source and target counts; after the run it MUST compare
  them. If a target count drops, the run MUST stop with a non-zero exit code and report.
- Every run MUST write a verification report: source vs target counts, skipped items with
  reasons, unresolved links, redirects and run time.

Rationale: a migration demo is only credible if its numbers can be checked and repeated.

### IV. Test-First Core

- Pure logic (form definition parsing, editable regions, plan building, link rewriting, redirect
  planning, count comparison) MUST be developed test-first: failing test, then code.
- Code that talks to SQL Server or Umbraco MUST have integration tests that run against the
  sample dataset and the local host, gated by environment variables so the unit suite runs
  without Docker.
- Formats owned by Umbraco (rich text link markup, property value shapes) MUST be checked
  against a running Umbraco 17 instance, not assumed from memory.

Rationale: the risky parts are the API formats and the URL rules; tests pin both.

### V. Minimal Code

- Write the minimum code that solves the brief. No speculative abstractions beyond
  `IKenticoReader`. No plugin systems, no generic mapping DSL.
- Site-specific choices (which paths are application pages, which folders, where brands live)
  live in a profile JSON file, not in code.
- Prefer one console project, one host project and one test project.

Rationale: the repository is read by prospective clients; clarity beats feature count.

### VI. Honest Numbers

- The README and docs MUST report what the tool actually did on the reference run, including
  what was skipped, what was not provided (missing listing photos) and what was thin (no images
  in rich text, zero media libraries on the reference site).
- Scope extensions agreed with the owner (listing lookups, product SKU fields) MUST be labelled
  as such.

Rationale: a migration service is sold on trust.

### VII. Small Verifiable Steps

- Every task MUST end with a check the owner can run (a test command, a `curl`, a count query)
  and a commit.
- No task MAY leave the solution in a non-building state.

Rationale: the owner reviews progress step by step and work is split across subagents.

## Technical Constraints

- C# on .NET 10 (`net10.0`), nullable enabled, warnings as errors.
- Target: Umbraco 17 LTS on SQL Server; media in Azure Blob Storage (Azurite emulator locally).
- Development machine: Apple Silicon MacBook; SQL Server 2022 and Azurite run in Docker.
- Configuration through `appsettings.json` and command-line overrides. Secrets and connection
  strings only through user-secrets or environment variables, never in the repository.
- Source schema supported: Kentico 11 (detected from `CMS_SettingsKey.CMSDBVersion`); any other
  version MUST fail fast with a clear message.

## Development Workflow

- Specification artifacts live in `specs/<feature>/` (spec, plan, research, data model,
  contracts, tasks). Decisions recorded in `docs/MAPPING.md` are binding for the spec.
- Tasks are small (30 to 90 minutes), carry exact file paths, a check and a commit, and mark
  which tasks can run in parallel.
- Commits use Conventional Commit prefixes. Pushing to the project's GitHub remote is allowed.
- All text written in the repository is in English and MUST NOT contain em dashes.

## Governance

- This constitution overrides other practices in this repository. A plan or task that breaks a
  principle MUST say so and justify it in the plan's Complexity Tracking section.
- Amendments: edit this file, bump the version (MAJOR for removed or redefined principles, MINOR
  for added principles or sections, PATCH for wording) and record the change in the commit
  message.
- Reviews of each task MUST check principles I, II and III explicitly.

**Version**: 1.0.0 | **Ratified**: 2026-11-14 | **Last Amended**: 2026-11-14
