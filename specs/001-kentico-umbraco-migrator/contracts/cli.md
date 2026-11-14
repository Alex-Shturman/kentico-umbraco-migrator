# Contract: Command-line interface

```
dotnet run --project src/KenticoMigrator -- <command> --profile <path> [options] [config overrides]
```

## Commands

| Command | Reads Kentico | Reads Umbraco | Writes Umbraco | Purpose |
|---|---|---|---|---|
| `counts` | yes | yes (if configured) | no | Print source and target counts per type |
| `plan` | yes | yes, if reachable | no | Preview: plan plus would-be outcome per item (FR-011); plan-only when Umbraco is not configured or not reachable |
| `migrate` | yes | yes | yes | Full run (FR-026 order) |

## Options

| Option | Default | Meaning |
|---|---|---|
| `--profile <path>` | required | Site profile JSON ([site-profile.md](site-profile.md)) |
| `--overwrite-edited` | off | Update items an editor changed in Umbraco (FR-015a) |
| `--run-dir <path>` | `runs/<yyyyMMdd-HHmmss>` | Where the report files go |

## Configuration keys

Bound from `appsettings.json`, then environment variables (`KM_` prefix, `__` as separator), then
user secrets (project `KenticoMigrator`), then command-line `--Key=Value`.

| Key | Example | Secret |
|---|---|---|
| `Kentico:ConnectionString` | `Server=localhost,1433;Database=Kentico11Sample;...` (port 1433 for your own container, 14333 for the compose service) | yes |
| `Kentico:MediaRoot` | `sample/media` | no |
| `Umbraco:BaseUrl` | `https://localhost:44317` (also the default when empty) | no |
| `Umbraco:ClientId` | `umbraco-back-office-migrator` (also the default when empty) | no |
| `Umbraco:ClientSecret` | | yes; setting it enables the target, without it the tool runs in plan-only mode |
| `Umbraco:AllowUntrustedCertificate` | `true` (Development only) | no |

`appsettings.json` in the repository holds only empty placeholders.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Finished, but one or more items failed |
| 2 | Configuration, connection, version or plan error, or a failure in the read phase, the plan phase or the first snapshot; nothing was written. A report is still written, with an error issue (exception type or `HTTP <status> <title>` only) |
| 3 | A target count dropped during the run (also when the run stopped with an exception after the first snapshot: the second snapshot is then taken on a best-effort basis and the guard runs; exit 3 wins over 1) |

## Console output

One line per phase with counts and elapsed time, warnings for skipped/edited/missing items in
summary form, the path of the run folder, and the exit code. No field content is printed.

## Run folder

| File | Content |
|---|---|
| `report.json` | `RunReport` ([data-model.md](../data-model.md) section 6) |
| `report.md` | Human-readable version: counts table, skipped by category, issues, links, redirects, missing in source, schedules, timings |
| `redirects.csv` | `old_url,new_url,target_key,reason` |
| `skipped.csv` | `category,identifier,old_path,reason` |
| `items.csv` | `kind,type,key,old_path,outcome,error` |

`redirects.csv` is the one file that may hold content-derived text: old URLs (a brand route contains the brand name by design). URLs are not field values in the FR-028 sense. `error` keeps the HTTP status and problem title only (first line, cut before any response body or quote, at most 200 characters). Sync code must put only `HTTP <status> <problem title>` into `ItemResult.Error`, never the problem `detail`; the report cut is a safety net, not a filter.

## Notes on re-runs

- **Sorting.** The children of a parent are put in the planned order only in a run that creates, updates or overwrites at
  least one child of that parent, and never when a child of that parent is left alone as edited (`EditedSkipped`). A failed sort
  (after the API client's own retries on server and network errors) is reported as a failed `sort` item, and it is repaired on a
  later run only when a child of that parent is written again. An order an editor sets is not undone by a run that writes nothing under
  that parent.
- **Publishing (FR-015a).** One rule: a page the run creates (or restores from the recycle bin) is published; a page the run only
  updates, moves or overwrites is published again only when it was published before the write (state `Published` or
  `PublishedPendingChanges` with a publish date). A page an editor unpublished stays unpublished even when its source text changed;
  it is listed as a review item ("planned as published but not published in Umbraco", with old path and key, in the Issues section
  of `report.md`), as is any page a plain re-run finds unpublished. The API client already retries server and network errors on the
  publish call; a page whose publish is refused is reported as failed. An update whose publish is refused (a non-transient answer
  such as "parent not published") stays as pending changes, is `Failed` on that run and `Unchanged` afterwards, because the tool
  never publishes pending changes on a plain re-run (they may be an editor's held-back draft). After fixing the cause, publish the
  page in the back office, or change the page and re-run with `--overwrite-edited` (or change it in the source): the page is written
  again and, having been published before, published (verified by `RerunEndToEndTests`). A page the plan does not publish is never published.
- **Media.** Folders and files carry no stored fingerprint (research R5-media): an item whose name and folder are the planned ones
  is `Unchanged`; one an editor renamed or moved is reported as edited and left alone unless `--overwrite-edited` is given, which
  moves it back, uploads the file again and sets the planned name. A rename in the source is reported the same way. File bytes
  are not compared and media that left the source are not listed as missing.
- **Redirects.** A redirect is registered once per old URL and page; a re-run reports it as existing. Redirects are never removed.
- **Document types.** Editor additions (properties, groups, allowed children, compositions) are kept and numbered after the
  planned ones. The tool owns a planned property's alias, name, data type, group and `mandatory` flag: a flag an editor changes is
  set back to the planned value on the next update of that type, while the pattern and messages of its validation are kept.
- **Templates.** The tool writes a fingerprint comment into each template. A template whose body no longer matches it is
  reported as "edited in Umbraco" and left alone unless `--overwrite-edited` is given.
