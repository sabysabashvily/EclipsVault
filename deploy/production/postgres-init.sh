#!/bin/sh
# Creates the vault's own login and gives it the database, so the vault never signs in as the
# superuser. The PostgreSQL image runs this once, on an empty data volume.
set -eu

# The password never appears in psql's arguments, in an error message or in the server log:
# \getenv reads it from the environment, terse errors leave out the failing line, and the server
# stops logging the statement that failed.
psql --set=ON_ERROR_STOP=1 --username="$POSTGRES_USER" --dbname="$POSTGRES_DB" <<'SQL'
\set VERBOSITY terse
\getenv app_password EV_DB_APP_PASSWORD
SET log_min_error_statement = panic;
CREATE ROLE eclipsvault_app LOGIN PASSWORD :'app_password';
ALTER DATABASE eclipsvault OWNER TO eclipsvault_app;
SQL
