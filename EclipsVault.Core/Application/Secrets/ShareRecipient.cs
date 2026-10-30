using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Secrets;

public static class ShareRecipient
{
    public static SecretGrant NewShare(Secret secret, User recipient, IAuditContext issuer, DateTimeOffset now, int? expiresInDays)
        => new()
        {
            Id = Guid.NewGuid(),
            SecretId = secret.Id,
            GranteeUserId = recipient.Id,
            GranteeUsername = recipient.Username,
            IssuerUserId = issuer.UserId,
            GrantedBy = issuer.Username ?? "system",
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresInDays is > 0 ? now.AddDays(expiresInDays.Value) : null,
        };

    public static void EnsureCanReceive(Secret secret, User recipient, Guid? issuerId)
    {
        if (recipient.Id == issuerId)
        {
            throw new SharingException("You already have access — you cannot share a secret with yourself.");
        }

        // A share lifts only the project rule, so it cannot help a recipient the clearance rule denies.
        if ((int)recipient.Clearance < (int)secret.Sensitivity)
        {
            throw new SharingException("That account's clearance is below this secret's sensitivity, so a share would not let them open it.");
        }
    }
}
