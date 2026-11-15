namespace EclipsVault.Core.Application.Auditing;

/// <summary>What verifying the audit trail found (ADR-0030).</summary>
public enum AuditChainOutcome
{
    /// <summary>The chain links, and every checkpoint signed by the current key matches it.</summary>
    Anchored,

    /// <summary>The chain links, but no checkpoint signed by the current key covers it.</summary>
    Unanchored,

    /// <summary>The chain is broken, or contradicts a checkpoint signed by the current key.</summary>
    Tampered
}
