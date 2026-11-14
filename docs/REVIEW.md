# Constitution review (T062)

Review of the production code (`src/KenticoMigrator`, `host/UmbracoSite`, `scripts`) against principles I, II and III of the
constitution, on 2026-11-14. Method: the two checks of T062, a review of the working-tree changes with `/code-review:code-review`
(no high-signal issue in the diff), and an independent read of all production code by a reviewer subagent that had no access to
the client material. Findings that are larger than a small fix are recorded as follow-up tasks T066 to T069 in
`specs/001-kentico-umbraco-migrator/tasks.md`; they are not fixed inside this task.

## Checks

| Check | Result |
|---|---|
| `git grep -nE "INSERT\|UPDATE\|DELETE\|MERGE" -- src/KenticoMigrator/Kentico` | prints nothing |
| `git grep -n "HttpMethod.Delete" -- src` | prints nothing (two uses were rewritten, see below) |
| `scripts/check-repo.sh` | OK |

The delete verb appeared twice in `src`: the idempotence rule of `ManagementApiClient` and the guard in `UmbracoApi` that refuses
it. Neither sends a delete. Both now compare the method name, so the check is a literal zero. No code path deletes or unpublishes.

## Principle I: client data private

- The tracked tree is compliant: no client content, only section-level paths, class and column names in
  `profiles/pontoonsonly.json`, aggregate numbers in the docs. `Sources/`, `sources/`, `_work/` and `runs/` are ignored.
- **Fixed in the git history (T069).** A planning document committed in the first commits, and one task text, contained a
  brand name from the client's data, and the first upload to GitHub included it. On the owner's decision the history was
  rewritten with `git filter-repo --replace-text` (the name replaced by an invented one in every commit) and force-uploaded.
  A scan of every commit for the name finds nothing, and a scan of all published file versions against the source's brand,
  user, listing, page and domain values (kept outside the repository and deleted afterwards) finds only common words, the
  client name and section-level paths. GitHub may keep unreferenced old commits in its cache for a while.
- Fixed: `*.zip`, `*.7z` and `*.bacpac` are now git-ignored (the client archives named in DISCOVERY.md would otherwise be staged by
  `git add .`).
- `scripts/check-repo.sh` checks paths and em dashes only; it cannot find client words. A denylist kept outside the repository
  would (part of T069).

## Principle II: read-only source, API-only target

Compliant. All SQL is in `Kentico11SqlReader` behind `IKenticoReader`, every statement is a SELECT with parameters or checked
identifiers, and the Umbraco database is never opened (the host endpoint uses `IRedirectUrlService` and `IContentService`).
`ApplicationIntent=ReadOnly` is only a routing hint on a plain server; the guarantee is that every query is a SELECT. A source
login with read permission only is the safer setup.

## Principle III: idempotent, verifiable runs

Compliant: item keys come from `Keys` (Kentico GUIDs or UUID v5; the only random GUID is the id of a temporary upload), nothing is
deleted or unpublished, snapshots are taken before and after, exit code 3 is returned when a count dropped in a normal run, and
the reports are written on plan errors, an unreachable target, failed items and a stop inside the run.

Not compliant in three places (follow-ups, all fixed on 2026-11-14: T066, T067, T068 are done in `tasks.md`):

| Task | Finding |
|---|---|
| T066 | An update of an unedited page that an editor had unpublished publishes it again when the source text changed (the "never publish on a re-run" rule covers the unchanged branch only). The "Notes on re-runs" say a page is published after it is created or updated, so the rule needs a decision, then a code and test change. |
| T067 | A failure in the read phase or in the first snapshot (SQL error, rejected token) ends the run with exit code 2 and no report folder. The constitution says every run writes a report. |
| T068 | If a run stops with an exception after the first snapshot, the count guard is not evaluated, so a real drop is reported as exit code 1 instead of 3. |

## Smaller observations, not tracked

- Data types written by the tool are overwritten when an editor changed their configuration, without `--overwrite-edited`
  (document types merge editor additions, templates carry a hash). Low risk: the data types are the tool's own editors.
- The media file existence check in `Program.cs` did not apply the rooted-path guard of the reader; a hostile database could make
  it probe a file outside the media root. Needs a malicious source. Fixed later: the reader and the check now share
  `Kentico/MediaPaths.cs`, which treats a rooted or escaping path as missing.
- The review hint "re-run with `--overwrite-edited`" for a page that is unpublished and unedited does not change anything.
