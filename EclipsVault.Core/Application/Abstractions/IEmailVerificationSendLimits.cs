namespace EclipsVault.Core.Application.Abstractions;

public interface IEmailVerificationSendLimits
{
    /// <summary>Reserves a send inside the caller's account transaction, shared across nodes.</summary>
    Task<bool> TryReserveAsync(Guid accountId, string sourceIp, DateTimeOffset now, CancellationToken ct);
}
