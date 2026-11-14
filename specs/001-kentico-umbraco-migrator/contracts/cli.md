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
