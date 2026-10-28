using System.Security.Cryptography;
using System.Text;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Persistence.Locking;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence;

public sealed class EmailVerificationSendLimits(EclipsVaultDbContext context, IAuditChainLocker locks) : IEmailVerificationSendLimits
{
    private const int MaximumSendsPerHour = 6;

    public async Task<bool> TryReserveAsync(Guid accountId, string sourceIp, DateTimeOffset now, CancellationToken ct)
    {
        // One short database lock makes the overlapping account/IP budgets atomic on every node.
        await locks.AcquireAsync(context, "EclipsVault:EmailSends", TimeSpan.FromSeconds(15), ct);
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceIp)));
        var hourAgo = now.AddHours(-1);
        var minuteAgo = now.AddMinutes(-1);
        await context.EmailVerificationSends.Where(send => send.SentAtUtc <= hourAgo).ExecuteDeleteAsync(ct);
        var recent = context.EmailVerificationSends.Where(send => send.AccountId == accountId || send.SourceHash == sourceHash);
        if (await recent.AnyAsync(send => send.SentAtUtc > minuteAgo, ct)
            || await recent.CountAsync(send => send.AccountId == accountId, ct) >= MaximumSendsPerHour
            || await recent.CountAsync(send => send.SourceHash == sourceHash, ct) >= MaximumSendsPerHour)
        {
            return false;
        }

        context.EmailVerificationSends.Add(new EmailVerificationSend
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            SourceHash = sourceHash,
            SentAtUtc = now,
        });
        return true;
    }
}
