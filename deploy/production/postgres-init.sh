#!/bin/sh
# Creates the vault's own login and gives it the database, so the vault never signs in as the
# superuser. The PostgreSQL image runs this once, on an empty data volume.
set -eu

# psql quotes :'app_password' as a string literal, so the password stays data and never becomes SQL.
psql --set=ON_ERROR_STOP=1 --username="$POSTGRES_USER" --dbname="$POSTGRES_DB" \
  --set=app_password="$EV_DB_APP_PASSWORD" <<'SQL'
CREATE ROLE eclipsvault_app LOGIN PASSWORD :'app_password';
ALTER DATABASE eclipsvault OWNER TO eclipsvault_app;
SQL
