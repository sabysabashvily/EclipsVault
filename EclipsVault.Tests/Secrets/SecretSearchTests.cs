using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.Secrets;

/// <summary>The palettes' secret search, as the Legacy search answered it.</summary>
public class SecretSearchTests
{
    private static readonly SecretSummaryDto[] Listable =
    [
        Row("Phoenix_Db_Password", "PHOENIX"),
        Row("Api_Phoenix_Token", "GLOBAL"),
        Row("Billing_Key", "PHOENIX"),
        Row("Key_A", "GLOBAL"),
        Row("Key_B", "GLOBAL"),
        Row("Key_C", "GLOBAL"),
        Row("Key_D", "GLOBAL"),
        Row("Key_E", "GLOBAL"),
        Row("Key_F", "GLOBAL"),
        Row("Key_G", "GLOBAL"),
        Row("Key_H", "GLOBAL"),
        Row("Key_I", "GLOBAL"),
    ];

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("p", "")]
    [InlineData(" p ", "")]
    [InlineData("pho", "Phoenix_Db_Password,Api_Phoenix_Token,Billing_Key")]
    [InlineData(" PHOENIX ", "Phoenix_Db_Password,Api_Phoenix_Token,Billing_Key")]
    [InlineData("bill", "Billing_Key")]
    [InlineData("key", "Key_A,Key_B,Key_C,Key_D,Key_E,Key_F,Key_G,Key_H")]
    [InlineData("global", "Api_Phoenix_Token,Key_A,Key_B,Key_C,Key_D,Key_E,Key_F,Key_G")]
    [InlineData("nothing-like-it", "")]
    public void Finds_by_name_or_project_after_two_characters_prefix_first_at_most_eight(string? query, string expected)
    {
        var found = SecretSearch.Find(Listable, query).Select(secret => secret.Name);

        Assert.Equal(expected, string.Join(",", found));
    }

    private static SecretSummaryDto Row(string name, string project) => new(
        Guid.NewGuid(), name, project, SecretEnvironment.Development, SensitivityLevel.Internal,
        DateTimeOffset.UnixEpoch, null);
}
