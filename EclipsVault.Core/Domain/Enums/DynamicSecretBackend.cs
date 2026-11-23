namespace EclipsVault.Core.Domain.Enums;

/// <summary>
/// The system a dynamic-secret role mints credentials on. Each value has one
/// <c>IDynamicSecretBackend</c> implementation in Infrastructure.
/// </summary>
public enum DynamicSecretBackend
{
    /// <summary>A real SQL Server login + database user, created and dropped on demand.</summary>
    SqlServer = 1,

    /// <summary>A real PostgreSQL login role, created and dropped on demand.</summary>
    Postgres = 2
}
