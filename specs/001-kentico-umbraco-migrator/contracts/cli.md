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
| `Kentico:ConnectionString` | `Server=localhost,1433;Database=Kentico11Sample;...` | yes |
| `Kentico:MediaRoot` | `sample/media` | no |
| `Umbraco:BaseUrl` | `https://localhost:44317` | no |
| `Umbraco:ClientId` | `umbraco-back-office-migrator` | no |
| `Umbraco:ClientSecret` | | yes |
| `Umbraco:AllowUntrustedCertificate` | `true` (Development only) | no |

`appsettings.json` in the repository holds only empty placeholders.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Finished, but one or more items failed |
| 2 | Configuration, connection, version or plan error; nothing was written |
| 3 | A target count dropped during the run; the run stopped |

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
  is retried once in the same run; if it still fails it is reported as a failed `sort` item, and it is repaired on a later run
  only when a child of that parent is written again. An order an editor sets is not undone by a run that writes nothing under
  that parent.
- **Publishing.** A page is published after it is created or updated. A server or network error on publish is retried once; a
  page that is still not published is reported as failed, and a later run publishes it when it was never published. Pending
  changes on an already published page are never published by the tool (they may be an editor's held-back draft), and a page an
  editor unpublished stays unpublished.
- **Templates.** The tool writes a fingerprint comment into each template. A template whose body no longer matches it is
  reported as "edited in Umbraco" and left alone unless `--overwrite-edited` is given.
