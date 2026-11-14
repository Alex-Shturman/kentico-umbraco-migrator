#!/usr/bin/env bash
# Creates (or recreates) the Kentico11Sample database in the SQL Server container and prints counts.
# Usage: SQL_CONTAINER=oib_sql_container scripts/restore-sample.sh
# The SA password is read inside the container from its own MSSQL_SA_PASSWORD and never printed.
set -euo pipefail

container="${SQL_CONTAINER:-kentico-migrator-sql}"
root="$(cd "$(dirname "$0")/.." && pwd)"
remote_sql="/tmp/kentico11-sample.sql"

# Runs sqlcmd inside the container; arguments after the first are passed to sqlcmd.
run_sqlcmd() {
  docker exec "$container" bash -c \
    '/opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P "$MSSQL_SA_PASSWORD" "$@"' _ "$@"
}

docker cp "$root/sample/kentico11-sample.sql" "$container:$remote_sql"
run_sqlcmd -i "$remote_sql" >/dev/null
docker exec -u 0 "$container" rm -f "$remote_sql"

echo "Documents per class:"
run_sqlcmd -d Kentico11Sample -W -h -1 -s ' ' -Q "
SET NOCOUNT ON;
SELECT c.ClassName, COUNT(*) FROM CMS_Document d
JOIN CMS_Tree n ON n.NodeID = d.DocumentNodeID
JOIN CMS_Class c ON c.ClassID = n.NodeClassID
GROUP BY c.ClassName ORDER BY c.ClassName;"

echo "Attachments (current): $(run_sqlcmd -d Kentico11Sample -W -h -1 -Q 'SET NOCOUNT ON; SELECT COUNT(*) FROM CMS_Attachment;')"
echo "Media files: $(run_sqlcmd -d Kentico11Sample -W -h -1 -Q 'SET NOCOUNT ON; SELECT COUNT(*) FROM Media_File;')"
echo "Aliases: $(run_sqlcmd -d Kentico11Sample -W -h -1 -Q 'SET NOCOUNT ON; SELECT COUNT(*) FROM CMS_DocumentAlias;')"
