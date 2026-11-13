# Database scripts

Two idempotent scripts, generated from the EF Core migrations and nothing else. Each file creates
the schema on an empty database, or brings a database that already has some of these migrations
forward to this commit. Running the same file again changes nothing.

| File | Engine | Migrations project |
|---|---|---|
| `sqlserver/migrations.sql` | SQL Server | `EclipsVault.Infrastructure` |
| `postgres/migrations.sql` | PostgreSQL | `EclipsVault.Migrations.Postgres` |

The vault applies these migrations itself when it starts in Development, and when
`Database:MigrateOnStartup` is true. These files are that same schema written out for review, for
a DBA, and for a manual install. A production deploy that leaves the setting off still applies the
schema from its own job; [`docs/INSTALL.md`](../docs/INSTALL.md) §3 describes that job.

Create an empty database first. The script does not create one, and the login that runs it needs
the right to create tables.

## Apply

SQL Server, with `sqlcmd`. `-b` stops on the first error instead of continuing into the next batch.
`-I` turns `QUOTED_IDENTIFIER` on, which the filtered indexes in this script require. `sqlcmd` leaves it off:

    sqlcmd -S db.example.internal -d eclipsvault -U <deploy login> -P '…' -I -b -i db/sqlserver/migrations.sql

PostgreSQL, with `psql`. `ON_ERROR_STOP` stops on the first error:

    psql "host=db.example.internal dbname=eclipsvault user=<deploy login>" -v ON_ERROR_STOP=1 -f db/postgres/migrations.sql

## Regenerate

From the repository root, after a migration in either set. Commit both files. The `migrations` job
runs these same commands and fails when a committed file differs.

The tool is the local `dotnet-ef` 10.0.9 in `dotnet-tools.json` (`rollForward` false), the same
version as `Microsoft.EntityFrameworkCore.Design`. `dotnet tool restore` installs it, and `dotnet ef`
from this directory uses that one. The `migrations` job does the same. SQL Server is the default,
so its command leaves `ECLIPSVAULT_DESIGN_PROVIDER` unset.

SQL Server compiles a batch before it runs, and the tool keeps a whole migration in one batch.
A migration that adds a column and then writes it fails on an empty database. `split_batches.py`
puts each operation in its own batch. The transaction stays open across `GO`.

SQL Server:

    dotnet tool restore && dotnet ef migrations script --idempotent -p EclipsVault.Infrastructure -s EclipsVault.Web -c EclipsVaultDbContext -o db/sqlserver/migrations.sql && python3 -I db/sqlserver/split_batches.py db/sqlserver/migrations.sql

PostgreSQL:

    dotnet tool restore && ECLIPSVAULT_DESIGN_PROVIDER=Postgres dotnet ef migrations script --idempotent -p EclipsVault.Migrations.Postgres -s EclipsVault.Web -c EclipsVaultDbContext -o db/postgres/migrations.sql
