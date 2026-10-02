#!/bin/sh
set -eu
# psql variables quote the password as a SQL literal; it is never printed.
task_app_password=$(cat /run/secrets/db_app_password)
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    --set ON_ERROR_STOP=1 --set app_password="$task_app_password" <<'SQL'
REVOKE ALL ON DATABASE testtask FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE ROLE testtask_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION
    PASSWORD :'app_password';
GRANT CONNECT ON DATABASE testtask TO testtask_app;
GRANT USAGE, CREATE ON SCHEMA public TO testtask_app;
SQL
unset task_app_password
