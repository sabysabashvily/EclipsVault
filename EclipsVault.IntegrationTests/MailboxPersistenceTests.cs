using System.Security.Cryptography;
using System.Text;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Notifications;
using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Notifications;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Repositories;
using EclipsVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

public abstract class MailboxPersistenceTests(AccountChangeDatabases databases)
{
    private const string Code = "B8063D1E058C08D4CCEE7F26A559D39F";
    private readonly InMemorySessionRevocationService _revocation = new(NullLogger<InMemorySessionRevocationService>.Instance);

    [Fact]
    public async Task Concurrent_valid_confirmations_commit_one_email_generation_and_event()
    {
        var pending = await PendingAsync("it-mailbox-race-valid");

        var results = await Task.WhenAll(ConfirmAsync(pending, Code), ConfirmAsync(pending, Code));

        Assert.Equal(["committed", "OperationNotFound"], results.Order(StringComparer.OrdinalIgnoreCase));
        await using var read = databases.NewRequest();
        Assert.Equal(pending.TargetEmail, (await read.LoadAsync(pending.UserId)).Email);
        Assert.Equal(1, (await read.LoadAsync(pending.UserId)).CredentialGeneration);
        Assert.Equal(1, await read.Context.AuditLogs.CountAsync(row => row.ResourceId == pending.UserId && row.Action == AuditAction.EmailChanged));
    }

    [Fact]
    public async Task Concurrent_wrong_confirmations_never_exceed_five_attempts_or_lock_sign_in()
    {
        var pending = await PendingAsync("it-mailbox-race-wrong");
        await using var setup = databases.NewRequest();
        await setup.Context.ProfileOperations.Where(row => row.Id == pending.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.MailboxFailures, 4));

        var results = await Task.WhenAll(ConfirmAsync(pending, "wrong"), ConfirmAsync(pending, "wrong"));

        Assert.Equal(["EmailCodeExpired", "EmailCodeInvalid"], results.Order());
        await using var read = databases.NewRequest();
        var stored = await read.Context.ProfileOperations.SingleAsync(row => row.Id == pending.Id);
        Assert.Equal(5, stored.MailboxFailures);
        Assert.Null(stored.MailboxCodeHash);
        Assert.Null(stored.VerifiedAtUtc);
        Assert.Equal(0, (await read.LoadAsync(pending.UserId)).FailedAccessCount);
    }

    [Fact]
    public async Task The_fifth_wrong_attempt_and_correct_confirmation_cannot_both_take_effect()
    {
        var pending = await PendingAsync("it-mailbox-race-fifth");
        await using var setup = databases.NewRequest();
        await setup.Context.ProfileOperations.Where(row => row.Id == pending.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.MailboxFailures, 4));

        var results = await Task.WhenAll(ConfirmAsync(pending, Code), ConfirmAsync(pending, "wrong"));

        Assert.Contains(results, result => result is "committed" or "EmailCodeInvalid");
        await using var read = databases.NewRequest();
        var account = await read.LoadAsync(pending.UserId);
        var remains = await read.Context.ProfileOperations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == pending.Id);
        Assert.True((account.CredentialGeneration == 1 && remains is null)
            || (account.CredentialGeneration == 0 && remains!.MailboxCodeHash is null && remains.VerifiedAtUtc is null));
        Assert.Equal(0, account.FailedAccessCount);
    }

    [Fact]
    public async Task Two_accounts_competing_for_the_same_canonical_mailbox_leave_one_generic_conflict()
    {
        var first = await PendingAsync("it-mailbox-unique-first", "Mailbox.Race@Example.Test");
        var second = await PendingAsync("it-mailbox-unique-second", "MAILBOX.RACE@EXAMPLE.TEST");

        var results = await Task.WhenAll(ConfirmAsync(first, Code), ConfirmAsync(second, Code));

        Assert.Equal(["committed", "EmailConflict"], results.Order(StringComparer.OrdinalIgnoreCase));
        await using var read = databases.NewRequest();
        Assert.Equal(1, await read.Context.Users.CountAsync(account => account.NormalizedEmail == "MAILBOX.RACE@EXAMPLE.TEST"));
    }

    [Fact]
    public async Task Revoking_the_session_retires_its_mailbox_authority_even_with_the_correct_code()
    {
        var pending = await PendingAsync("it-mailbox-revoked");
        await _revocation.RevokeAsync(pending.UserId);

        Assert.Equal("OperationChanged", await ConfirmAsync(pending, Code));

        await using var read = databases.NewRequest();
        Assert.Equal(0, (await read.LoadAsync(pending.UserId)).CredentialGeneration);
    }

    [Fact]
    public async Task A_retired_credential_generation_cannot_activate_a_pending_mailbox()
    {
        var pending = await PendingAsync("it-mailbox-generation");
        await using var setup = databases.NewRequest();
        await setup.Context.Users.Where(account => account.Id == pending.UserId)
            .ExecuteUpdateAsync(set => set.SetProperty(account => account.CredentialGeneration, 1));

        Assert.Equal("OperationChanged", await ConfirmAsync(pending, Code));

        Assert.NotEqual(pending.TargetEmail, (await setup.LoadAsync(pending.UserId)).Email);
    }

    [Fact]
    public async Task Replacing_a_pending_target_in_another_session_removes_the_old_code_and_grant()
    {
        var pending = await PendingAsync("it-mailbox-replace");
        var replacement = new ProfileOperation
        {
            Id = Guid.NewGuid(),
            UserId = pending.UserId,
            SessionId = Guid.NewGuid(),
            Kind = ProfileOperationKind.ChangeEmail,
            TargetEmail = "different.target@example.test",
            RevocationVersion = pending.RevocationVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
        };
        await using var setup = databases.NewRequest();
        await setup.ProfileOperations.ReplaceForSessionAsync(replacement, DateTimeOffset.UtcNow, default);

        Assert.Equal("OperationNotFound", await ConfirmAsync(pending, Code));
        Assert.Equal(1, await setup.Context.ProfileOperations.CountAsync(row => row.UserId == pending.UserId));
    }

    [Fact]
    public async Task Fresh_account_verification_renews_only_the_same_pending_mailbox_grant()
    {
        var pending = await PendingAsync("it-mailbox-provider-reverify");
        var hasher = new Argon2idPasswordHasher(Options.Create(new Argon2Options { Iterations = 1, MemoryKb = 1024, Parallelism = 1 }));
        var password = hasher.Hash("Provider-current-password!");
        await using var setup = databases.NewRequest();
        var account = await setup.LoadAsync(pending.UserId);
        account.PasswordHash = password.Hash;
        account.PasswordSalt = password.Salt;
        await setup.Context.SaveChangesAsync();
        await setup.Context.ProfileOperations.Where(row => row.Id == pending.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.VerifiedAtUtc, DateTimeOffset.UtcNow.AddMinutes(-6)));
        Assert.Equal("VerificationExpired", await ConfirmAsync(pending, Code));
        await using var request = databases.NewRequest();
        var verification = new AccountVerification(request.Users, request.ProfileOperations, request.RecoveryCodes,
            new PasskeyCredentialRepository(request.Context), null!, hasher, request.SecondFactors, request.Attempt,
            new AuthenticationFailures(request.Users, request.Attempt, LockoutPolicy.Default, TimeProvider.System), _revocation, TimeProvider.System);

        await verification.VerifyWithCodeAsync(new ProfileSession(pending.UserId, pending.SessionId,
            new SessionStamp(0, pending.RevocationVersion)), pending.Id, AccountProofMethod.PasswordAndAuthenticatorCode,
            "Provider-current-password!", Totp.NextCode(account.TotpSecret!), default);

        Assert.Equal("committed", await ConfirmAsync(pending, Code));
        await using var read = databases.NewRequest();
        Assert.Equal(pending.TargetEmail, (await read.LoadAsync(pending.UserId)).Email);
    }

    [Fact]
    public async Task An_expired_mailbox_retires_its_grant_in_the_database()
    {
        var pending = await PendingAsync("it-mailbox-provider-expiry");
        await using var setup = databases.NewRequest();
        await setup.Context.ProfileOperations.Where(row => row.Id == pending.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.MailboxExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));

        Assert.Equal("EmailCodeExpired", await ConfirmAsync(pending, Code));

        await using var read = databases.NewRequest();
        var operation = await read.Context.ProfileOperations.SingleAsync(row => row.Id == pending.Id);
        Assert.Null(operation.VerifiedAtUtc);
        Assert.Null(operation.MailboxCodeHash);
        Assert.Equal(0, (await read.LoadAsync(pending.UserId)).CredentialGeneration);
    }

    [Fact]
    public async Task A_cancelled_mailbox_operation_cannot_consume_its_former_proofs()
    {
        var pending = await PendingAsync("it-mailbox-provider-cancel");
        await using var request = databases.NewRequest();
        Assert.True(await request.ProfileOperations.DeleteAsync(pending.Id, pending.UserId, pending.SessionId, default));

        Assert.Equal("OperationNotFound", await ConfirmAsync(pending, Code));

        Assert.Equal(0, (await request.LoadAsync(pending.UserId)).CredentialGeneration);
    }

    [Fact]
    public async Task Resend_and_confirmation_serialize_before_either_can_spend_the_old_mailbox_code()
    {
        var pending = await PendingAsync("it-mailbox-provider-resend-race");
        await using var capture = new SmtpCapture();
        await capture.StartAsync();
        var options = Options.Create(new EmailOptions
        {
            Enabled = true,
            Sender = "Smtp",
            Smtp = new SmtpOptions { Host = capture.Host, Port = capture.Port },
        });
        var delivery = new EmailVerificationDelivery(new SmtpEmailSender(options), options);

        var results = await Task.WhenAll(ResendAsync(pending, delivery), ConfirmAsync(pending, Code));

        Assert.Contains(results, result => result is "resent" or "committed");
        await using var read = databases.NewRequest();
        var account = await read.LoadAsync(pending.UserId);
        var remaining = await read.Context.ProfileOperations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == pending.Id);
        Assert.True((account.CredentialGeneration == 1 && remaining is null)
            || (account.CredentialGeneration == 0 && remaining!.MailboxDelivered
                && !remaining.MailboxCodeHash!.SequenceEqual(pending.MailboxCodeHash!)));
    }

    private async Task<string> ResendAsync(ProfileOperation operation, IEmailVerificationDelivery delivery)
    {
        await using var request = databases.NewRequest();
        var changes = new ProfileEmailChanges(request.Users, request.ProfileOperations, request.Attempt,
            request.EmailLimits, delivery, _revocation, null!, TimeProvider.System);
        try
        {
            await changes.RequestAsync(new ProfileSession(operation.UserId, operation.SessionId,
                new SessionStamp(0, operation.RevocationVersion)), operation.Id, "198.18.88.100", true, default);
            return "resent";
        }
        catch (ProfileOperationRefusedException refused)
        {
            return refused.Refusal.ToString();
        }
    }

    [Fact]
    public async Task Provisioning_that_wins_after_the_availability_check_leaves_activation_unchanged()
    {
        var pending = await PendingAsync("it-mailbox-constraint-race");
        var beforeWrite = new ActivationWriteGate();
        var activating = ConfirmAsync(pending, Code, beforeWrite);
        await beforeWrite.Reached.Task;
        try
        {
            await using var provisioning = databases.NewRequest();
            await provisioning.Users.AddAsync(EmailIdentityDatabases.NewAccount("it-mailbox-constraint-taker", pending.TargetEmail!.ToUpperInvariant()), default);
        }
        finally
        {
            beforeWrite.Release.SetResult();
        }

        Assert.Equal("EmailConflict", await activating);

        await using var read = databases.NewRequest();
        Assert.Equal(0, (await read.LoadAsync(pending.UserId)).CredentialGeneration);
        Assert.NotNull((await read.Context.ProfileOperations.SingleAsync(row => row.Id == pending.Id)).VerifiedAtUtc);
    }

    private sealed class ActivationWriteGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Reached.SetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    private async Task<ProfileOperation> PendingAsync(string username, string? target = null)
    {
        var account = await databases.CreateEnrolledAccountAsync(username);
        var operation = new ProfileOperation
        {
            Id = Guid.NewGuid(),
            UserId = account.Id,
            SessionId = Guid.NewGuid(),
            Kind = ProfileOperationKind.ChangeEmail,
            TargetEmail = target ?? username + ".new@example.test",
            RevocationVersion = await _revocation.CaptureAsync(account.Id),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15),
            VerifiedAtUtc = DateTimeOffset.UtcNow,
            ProofMethod = AccountProofMethod.PasswordAndAuthenticatorCode,
            MailboxCodeHash = SHA256.HashData(Encoding.UTF8.GetBytes(Code)),
            MailboxDelivered = true,
            MailboxExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15),
        };
        await using var request = databases.NewRequest();
        await request.ProfileOperations.ReplaceForSessionAsync(operation, DateTimeOffset.UtcNow, default);
        return operation;
    }

    private async Task<string> ConfirmAsync(ProfileOperation operation, string code, IInterceptor? fault = null)
    {
        await using var request = databases.NewRequest(fault);
        var notices = new NotificationService(new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance),
            new EmailLogRepository(request.Context), request.Users, new NotificationOptions(false), TimeProvider.System);
        var changes = new ProfileEmailChanges(request.Users, request.ProfileOperations, request.Attempt,
            null!, null!, _revocation, notices, TimeProvider.System);
        try
        {
            await changes.ConfirmAsync(new ProfileSession(operation.UserId, operation.SessionId,
                new SessionStamp(0, operation.RevocationVersion)), operation.Id, code, default);
            return "committed";
        }
        catch (ProfileOperationRefusedException refused)
        {
            return refused.Refusal.ToString();
        }
        catch (EmailAddressTakenException)
        {
            return "EmailConflict";
        }
    }
}

[Collection(SqlServerAccountChangeCollection.Name)]
public sealed class SqlServerMailboxPersistenceTests(SqlServerAccountChangeDatabases databases) : MailboxPersistenceTests(databases);

[Collection(PostgresAccountChangeCollection.Name)]
public sealed class PostgresMailboxPersistenceTests(PostgresAccountChangeDatabases databases) : MailboxPersistenceTests(databases);
