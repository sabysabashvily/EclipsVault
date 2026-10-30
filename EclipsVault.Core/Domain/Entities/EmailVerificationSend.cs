namespace EclipsVault.Core.Domain.Entities;

public sealed class EmailVerificationSend
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string SourceHash { get; set; } = string.Empty;

    public DateTimeOffset SentAtUtc { get; set; }
}
