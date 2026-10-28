namespace EclipsVault.Core.Application.Abstractions;

public interface IEmailVerificationDelivery
{
    bool Available { get; }

    /// <summary>Delivers through real SMTP without recording the code, body or transport error.</summary>
    Task<bool> TrySendAsync(string address, string code, CancellationToken ct);
}
