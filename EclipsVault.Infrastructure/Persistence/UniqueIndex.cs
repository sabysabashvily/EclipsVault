using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EclipsVault.Infrastructure.Persistence;

internal static class UniqueIndex
{
    private const int DuplicateKeyInUniqueIndex = 2601;
    private const int DuplicateKeyInUniqueConstraint = 2627;

    // SQL Server names the violated index only in its message; PostgreSQL reports it as the constraint.
    public static bool IsViolatedBy(DbUpdateException failure, string index) => failure.InnerException switch
    {
        SqlException sql => sql.Number is DuplicateKeyInUniqueIndex or DuplicateKeyInUniqueConstraint
                            && sql.Message.Contains(index, StringComparison.Ordinal),
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation
                                      && postgres.ConstraintName == index,
        _ => false,
    };
}
