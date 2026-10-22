using System.Text;

namespace EclipsVault.Infrastructure.Persistence;

/// <summary>
/// <see cref="Core.Domain.EmailIdentity.Normalize"/> written in each database's SQL. A database's own
/// UPPER depends on its collation or locale, so the casing comes from .NET's invariant rules instead.
/// </summary>
public static class EmailIdentitySql
{
    private static readonly (string Letters, string Uppercase) CaseMapping = BuildCaseMapping();

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
           $"N'{CaseMapping.Letters}', N'{CaseMapping.Uppercase}')";

    public static string NormalizePostgres(string column)
        => $"translate(btrim({column}, {PostgresWhiteSpace}), " +
           $"'{CaseMapping.Letters}', '{CaseMapping.Uppercase}') COLLATE \"C\"";

    private static (string Letters, string Uppercase) BuildCaseMapping()
    {
        var letters = new StringBuilder();
        var uppercase = new StringBuilder();
        for (var unit = char.MinValue; unit < char.MaxValue; unit++)
        {
            var folded = char.ToUpperInvariant(unit);
            if (folded != unit)
            {
                letters.Append(unit);
                uppercase.Append(folded);
            }
        }

        return (letters.ToString(), uppercase.ToString());
    }
}
