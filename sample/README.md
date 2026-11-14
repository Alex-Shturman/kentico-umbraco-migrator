# Sample Kentico 11 database

An invented kayak shop that looks like a real Kentico 11 database: same table and column names and
types (limited to what the reader uses), real `ClassFormDefinition` and `DocumentContent` XML
formats. Nothing here comes from a client.

## Restore

```bash
SQL_CONTAINER=oib_sql_container scripts/restore-sample.sh
```

The script copies `kentico11-sample.sql` into the container, runs it with the container's own
`MSSQL_SA_PASSWORD` (never printed) and prints the counts below. It drops and recreates the
database `Kentico11Sample`.

## Expected counts

| Item | Count |
|---|---|
| `CMS.Root` documents | 1 |
