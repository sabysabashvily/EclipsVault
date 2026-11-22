using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Tests.TestDoubles;

/// <summary>
/// A SQL Server target as a mint leaves it: the recipe runs statement by statement without a
/// transaction, so a failure can come after CREATE LOGIN or CREATE USER already ran.
/// </summary>
public sealed class FakeDynamicTarget : IDynamicSecretBackend
{
    /// <summary>How many of CREATE LOGIN and CREATE USER run before <see cref="MintFailure"/> is thrown.</summary>
    public int StatementsThatRun { get; init; } = 2;

    public Exception? MintFailure { get; init; }

    public Exception? RevokeFailure { get; set; }

    public HashSet<string> Logins { get; } = [];

    public HashSet<string> Users { get; } = [];

    public List<string> Passwords { get; } = [];

    public List<CancellationToken> RevokeTokens { get; } = [];

    public DynamicSecretBackend Backend => DynamicSecretBackend.SqlServer;

    public Task MintAsync(DynamicSecretRole role, string identity, string password, DateTimeOffset expiresAtUtc, CancellationToken ct)
    {
        Passwords.Add(password);
        if (StatementsThatRun >= 1) Logins.Add(identity);
        if (StatementsThatRun >= 2) Users.Add(identity);
        return MintFailure is null ? Task.CompletedTask : throw MintFailure;
    }

    public Task RevokeAsync(DynamicSecretRole role, string identity, CancellationToken ct)
    {
        RevokeTokens.Add(ct);
        ct.ThrowIfCancellationRequested();
        if (RevokeFailure is not null) throw RevokeFailure;
        Users.Remove(identity);
        Logins.Remove(identity);
        return Task.CompletedTask;
    }
}
