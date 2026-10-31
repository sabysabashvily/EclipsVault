namespace EclipsVault.Core.Domain.Enums;

/// <summary>Why an access request may not be filed. Approval refuses for ShareCannotHelp and AlreadyAbleToOpen too.</summary>
public enum AccessRequestRefusal
{
    /// <summary>The requester's clearance is below the secret's sensitivity, and a share lifts only the project rule.</summary>
    ShareCannotHelp,

    /// <summary>The requester's project, clearance or an active share already covers the secret.</summary>
    AlreadyAbleToOpen,

    AlreadyPending,
}
