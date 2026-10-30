namespace EclipsVault.Core.Domain.Enums;

/// <summary>Why an access request may not be filed, or, for the first two, approved.</summary>
public enum AccessRequestRefusal
{
    /// <summary>The requester's clearance is below the secret's sensitivity, and a share lifts only the project rule.</summary>
    ShareCannotHelp,

    /// <summary>The requester's project, clearance or an active share already covers the secret.</summary>
    AlreadyAbleToOpen,

    AlreadyPending,
}
