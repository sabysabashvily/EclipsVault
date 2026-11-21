namespace EclipsVault.Core.Application.Abac;

/// <summary>
/// Who may change or share a secret they can open: an administrator, or a member of the secret's project.
/// A share lets its recipient open the secret and nothing more.
/// </summary>
public static class SecretChangeRight
{
    public static bool Allows(bool isAdministrator, string subjectProject, string secretProject)
        => isAdministrator || string.Equals(subjectProject, secretProject, StringComparison.OrdinalIgnoreCase);
}
