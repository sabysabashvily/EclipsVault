namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>Minting failed and the vault removed whatever the recipe had created, so nothing was issued.</summary>
public sealed class MintRefusedException(string roleName)
    : DomainException($"The backend refused to mint a credential for '{roleName}'. Nothing was issued.");
