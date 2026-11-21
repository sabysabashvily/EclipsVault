using EclipsVault.Core.Application.Abac;
using Xunit;

namespace EclipsVault.Tests.Abac;

/// <summary>
/// Rotating, rotating upstream, restoring, deleting and sharing a secret the caller can open need the
/// change right. A share's recipient, from another project, can open the secret but not change it.
/// </summary>
public class SecretChangeRightTests
{
    [Theory]
    [InlineData(true, "PHOENIX", "PHOENIX", true)]
    [InlineData(true, "ELSEWHERE", "PHOENIX", true)]
    [InlineData(false, "PHOENIX", "PHOENIX", true)]
    [InlineData(false, "phoenix", "PHOENIX", true)]
    [InlineData(false, "ELSEWHERE", "PHOENIX", false)]
    public void Only_an_administrator_or_a_member_of_the_secrets_project_may_change_it(
        bool isAdministrator, string subjectProject, string secretProject, bool allowed)
        => Assert.Equal(allowed, SecretChangeRight.Allows(isAdministrator, subjectProject, secretProject));
}
