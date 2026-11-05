namespace EclipsVault.Core.Domain.Enums;

/// <summary>Whether an API key authenticates now. A revoked key reads as revoked even after its expiry passes.</summary>
public enum ApiKeyStatus
{
    Active,
    Expired,
    Revoked
}
