using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Notifications;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.Notifications;

/// <summary>
/// The lifecycle worker only stamps its "already warned" marker when this returns true, so the
/// return value decides between "never warn again" and "retry every minute forever". These pin that
/// contract at each boundary: recorded (even when delivery failed) vs. not recorded at all.
/// </summary>
public class ExpiringSecretNotificationTests
{
    private static readonly Guid OwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Expiry = DateTimeOffset.UtcNow.AddDays(3);

    private sealed class FakeSender : IEmailSender
    {
        private readonly bool _throws;
        public FakeSender(bool throws = false) => _throws = throws;
        public List<EmailMessage> Sent { get; } = [];
        public string Transport => "Fake";

        public Task SendAsync(EmailMessage message, CancellationToken ct)
        {
            if (_throws)
            {
                throw new InvalidOperationException("smtp is down");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOutbox : IEmailLogRepository
    {
        public List<EmailLog> Rows { get; } = [];
        public Task AddAsync(EmailLog entry, CancellationToken ct)
        {
            Rows.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EmailLog>> ListRecentAsync(int max, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<EmailLog>>(Rows);
    }

    private sealed class FakeUsers : IUserRepository
    {
        private readonly User? _user;
        public FakeUsers(User? user) => _user = user;

        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(_user);
        public Task<bool> ReloadAsync(User user, CancellationToken ct) => throw new NotSupportedException();

        public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarAsync(User user, byte[] png, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarRemovalAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryAcceptTotpStepAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryCompleteTotpEnrollmentAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RecordFailedAttemptAsync(User user, LockoutPolicy lockout, DateTimeOffset nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearFailedAttemptsAsync(User user, CancellationToken ct) => throw new NotSupportedException();
    }

    private static User Owner() => new()
    {
        Id = OwnerId,
        Username = "dev-user",
        DisplayName = "Devon Rhodes",
        Email = "devon.rhodes@eclipsvault.local"
    };

    private static NotificationService Build(FakeSender sender, FakeOutbox outbox, User? owner, bool enabled = true)
        => new(sender, outbox, new FakeUsers(owner), new NotificationOptions(enabled), TimeProvider.System);

    [Fact]
    public async Task Records_the_notice_and_reports_it_so_the_worker_marks_it_sent()
    {
        var sender = new FakeSender();
        var outbox = new FakeOutbox();

        var recorded = await Build(sender, outbox, Owner())
            .NotifyExpiringSecretAsync(OwnerId, "prod/api-key", Expiry, CancellationToken.None);

        Assert.True(recorded);
        var mail = Assert.Single(sender.Sent);
        Assert.Equal("devon.rhodes@eclipsvault.local", mail.To);
        Assert.Contains("prod/api-key", mail.Subject);

        var row = Assert.Single(outbox.Rows);
        Assert.Equal("SecretExpiring", row.EventType);
        Assert.Equal(EmailDeliveryStatus.Sent, row.Status);
    }

    [Fact]
    public async Task Tells_the_owner_that_rotating_alone_will_not_save_the_secret()
    {
        var sender = new FakeSender();

        await Build(sender, new FakeOutbox(), Owner())
            .NotifyExpiringSecretAsync(OwnerId, "prod/api-key", Expiry, CancellationToken.None);

        // The advice has to name the renewal, since rotating without one leaves the deadline intact.
        Assert.Contains("renewal period", Assert.Single(sender.Sent).Body);
    }

    [Fact]
    public async Task A_failed_delivery_still_counts_as_recorded_so_the_worker_does_not_loop()
    {
        // The outbox row — visible on the admin Notifications page — is the record of the notice.
        // Retrying a dead transport every minute for a week would bury that page instead.
        var outbox = new FakeOutbox();

        var recorded = await Build(new FakeSender(throws: true), outbox, Owner())
            .NotifyExpiringSecretAsync(OwnerId, "prod/api-key", Expiry, CancellationToken.None);

        Assert.True(recorded);
        var row = Assert.Single(outbox.Rows);
        Assert.Equal(EmailDeliveryStatus.Failed, row.Status);
        Assert.Equal("Mail delivery failed.", row.Error);
    }

    [Fact]
    public async Task A_suppressed_notice_is_still_recorded()
    {
        var sender = new FakeSender();
        var outbox = new FakeOutbox();

        var recorded = await Build(sender, outbox, Owner(), enabled: false)
            .NotifyExpiringSecretAsync(OwnerId, "prod/api-key", Expiry, CancellationToken.None);

        Assert.True(recorded);
        Assert.Empty(sender.Sent);
        Assert.Equal(EmailDeliveryStatus.Suppressed, Assert.Single(outbox.Rows).Status);
    }

    [Fact]
    public async Task An_unknown_owner_records_nothing_and_reports_it_so_the_worker_retries()
    {
        var outbox = new FakeOutbox();

        var recorded = await Build(new FakeSender(), outbox, owner: null)
            .NotifyExpiringSecretAsync(OwnerId, "prod/api-key", Expiry, CancellationToken.None);

        Assert.False(recorded);
        Assert.Empty(outbox.Rows);
    }
}
