namespace EclipsVault.Infrastructure.Persistence;

/// <summary>
/// <see cref="Core.Domain.EmailIdentity.Normalize"/> written in each database's SQL. A database's own
/// UPPER depends on its collation or locale, so the letters are translated explicitly instead.
/// </summary>
public static class EmailIdentitySql
{
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // The characters string.Trim() removes.
    private static readonly int[] WhiteSpaceCodePoints =
    [
        9, 10, 11, 12, 13, 32, 133, 160, 5760, 8192, 8193, 8194, 8195, 8196, 8197,
        8198, 8199, 8200, 8201, 8202, 8232, 8233, 8239, 8287, 12288
    ];

    private static readonly string SqlServerWhiteSpace =
        $"CONCAT({string.Join(", ", WhiteSpaceCodePoints.Select(codePoint => $"NCHAR({codePoint})"))})";

    private static readonly string PostgresWhiteSpace =
        $"concat({string.Join(", ", WhiteSpaceCodePoints.Select(codePoint => $"chr({codePoint})"))})";

    public static string NormalizeSqlServer(string column)
        => $"TRANSLATE(TRIM({SqlServerWhiteSpace} FROM {column} COLLATE Latin1_General_100_BIN2), " +
           $"N'{Lowercase}', N'{Uppercase}')";

    public static string NormalizePostgres(string column)
        => $"translate(btrim({column}, {PostgresWhiteSpace}), '{Lowercase}', '{Uppercase}') COLLATE \"C\"";
}
