#!/usr/bin/env bash
# Creates (or recreates) the Kentico11Sample database in the SQL Server container and prints counts.
# Usage: SQL_CONTAINER=<container> scripts/restore-sample.sh   (default kentico-migrator-sql, the docker-compose service)
# The SA password is read inside the container from its own MSSQL_SA_PASSWORD (passed to sqlcmd through
# SQLCMDPASSWORD, so it is neither on a command line nor printed).
set -euo pipefail

container="${SQL_CONTAINER:-kentico-migrator-sql}"
root="$(cd "$(dirname "$0")/.." && pwd)"
remote_sql="/tmp/kentico11-sample.sql"

# Runs sqlcmd inside the container; arguments are passed to sqlcmd. On failure the output is shown.
run_sqlcmd() {
  local output
  if ! output=$(docker exec "$container" bash -c \
    'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa "$@"' _ "$@" 2>&1); then
    echo "sqlcmd failed:" >&2
    echo "$output" >&2
    return 1
  fi
  printf '%s\n' "$output"
}

# Runs a query that returns one number and prints it; fails when the query fails or returns nothing.
scalar() {
  local value
  value=$(run_sqlcmd -d Kentico11Sample -W -h -1 -Q "SET NOCOUNT ON; $1" | tr -d ' \r')
  if [ -z "$value" ]; then
    echo "Query returned no value: $1" >&2
    return 1
  fi
  printf '%s' "$value"
}

# The copied script is removed on every exit path.
cleanup() { docker exec -u 0 "$container" rm -f "$remote_sql" >/dev/null 2>&1 || true; }
trap cleanup EXIT

docker cp "$root/sample/kentico11-sample.sql" "$container:$remote_sql"
run_sqlcmd -i "$remote_sql" >/dev/null

echo "Documents per class:"
run_sqlcmd -d Kentico11Sample -W -h -1 -s ' ' -Q "
SET NOCOUNT ON;
SELECT c.ClassName, COUNT(*) FROM CMS_Document d
JOIN CMS_Tree n ON n.NodeID = d.DocumentNodeID
JOIN CMS_Class c ON c.ClassID = n.NodeClassID
GROUP BY c.ClassName ORDER BY c.ClassName;"

attachments=$(scalar 'SELECT COUNT(*) FROM CMS_Attachment;')
media_files=$(scalar 'SELECT COUNT(*) FROM Media_File;')
links=$(scalar "SELECT SUM((LEN(c) - LEN(REPLACE(c, 'href=', ''))) / 5) FROM (SELECT CAST(DocumentContent AS nvarchar(max)) AS c FROM CMS_Document UNION ALL SELECT CAST(ArticleText AS nvarchar(max)) FROM CONTENT_Article) x;")
aliases=$(scalar 'SELECT COUNT(*) FROM CMS_DocumentAlias;')

echo "Attachments (current): $attachments"
echo "Media files: $media_files"
echo "Links (href= in page content and article text): $links"
echo "Aliases: $aliases"
