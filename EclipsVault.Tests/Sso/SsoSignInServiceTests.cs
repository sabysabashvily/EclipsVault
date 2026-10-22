using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Sso;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.Sso;

/// <summary>
/// SSO hands the vault an assertion from a system it does not administer, so every one of these is
/// about what the vault refuses to take on trust: the IdP proves who you are, and the vault decides
/// whether you may in, whether you are done authenticating, and what you may read.
///
/// The alternative — attributes flowing from the IdP — means an IdP administrator who mints
/// clearance=TopSecret reads every secret here, and the trail records it as legitimate.
/// </summary>
public class SsoSignInServiceTests
{
    private sealed class FakeUsers : IUserRepository
    {
        private readonly User? _user;
        public FakeUsers(User? user) => _user = user;

        public Task<User?> FindByEmailAsync(string email, CancellationToken ct)
            => Task.FromResult(_user);

        public Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct) => throw new NotSupportedException();

        public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => Task.FromResult<User?>(null);
        public Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<User?>(null);
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<User>>([]);
        public Task AddAsync(User user, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(User user, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(User user, CancellationToken ct) => Task.CompletedTask;
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => Task.FromResult<byte[]?>(null);
        public Task SetAvatarAsync(User user, byte[] png, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveAvatarAsync(User user, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingAudit : IAuditSink
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private const string Email = "dev-user@eclipsvault.local";

    private static User VaultUser(bool disabled = false) => new()
    {
        Id = Guid.NewGuid(),
        Username = "dev-user",
        DisplayName = "Dev User",
        Email = Email,
        Clearance = ClearanceLevel.Standard,
        ProjectKey = "PHOENIX",
        TotpEnabled = true,
        IsDisabled = disabled
    };

    private static ExternalIdentity Identity(
        string? email = Email, bool verified = true, params string[] amr) =>
        new("https://idp.example/realms/eclipsvault", "idp-subject-123", email, verified, amr);

    private static (SsoSignInService Service, RecordingAudit Audit) Build(User? user, bool trustIdpMfa = false)
    {
        var audit = new RecordingAudit();
        return (new SsoSignInService(new FakeUsers(user), audit, new SsoPolicy(trustIdpMfa)), audit);
    }

    [Fact]
    public async Task A_real_idp_user_with_no_vault_account_is_refused()
    {
        // The fixture the Keycloak realm ships as idp-only-user. The IdP vouches for them entirely;
        // it does not get to decide who may into this vault.
        var (service, audit) = Build(user: null);

        var result = await service.SignInAsync(Identity(), CancellationToken.None);

        Assert.Equal(SsoOutcome.NoVaultAccount, result.Outcome);
        Assert.Null(result.User);
        Assert.Contains(audit.Entries, e => e.Action == AuditAction.SsoSignInRefused);
    }

    [Fact]
    public async Task An_unverified_email_is_refused_as_critical()
    {
        // The link is the email, so an IdP that lets someone self-assert an address lets them assert
        // somebody else's. Matching on it would hand them that account.
        var (service, audit) = Build(VaultUser());

        var result = await service.SignInAsync(Identity(verified: false), CancellationToken.None);

        Assert.Equal(SsoOutcome.EmailNotVerified, result.Outcome);
        Assert.Null(result.User);
        Assert.True(Assert.Single(audit.Entries, e => e.Action == AuditAction.SsoSignInRefused).IsCritical);
    }

    [Fact]
    public async Task An_idp_that_sends_no_email_is_refused()
    {
        var (service, _) = Build(VaultUser());

        var result = await service.SignInAsync(Identity(email: null), CancellationToken.None);

        Assert.Equal(SsoOutcome.NoEmail, result.Outcome);
    }

    [Fact]
    public async Task A_disabled_account_is_refused_however_happy_the_idp_is()
    {
        var (service, _) = Build(VaultUser(disabled: true));

        var result = await service.SignInAsync(Identity(), CancellationToken.None);

        Assert.Equal(SsoOutcome.Disabled, result.Outcome);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task A_matched_user_keeps_the_clearance_the_vault_gave_them()
    {
        // The heart of it: the IdP said nothing about clearance and could not have. These attributes
        // come from the vault's own record, which is what makes an IdP compromise survivable.
        var user = VaultUser();
        var (service, audit) = Build(user);

        var result = await service.SignInAsync(Identity(), CancellationToken.None);

        Assert.Equal(SsoOutcome.Linked, result.Outcome);
        Assert.Equal(ClearanceLevel.Standard, result.User!.Clearance);
        Assert.Equal("PHOENIX", result.User.ProjectKey);
        Assert.Equal(user.Id, result.User.Id);
        Assert.Contains(audit.Entries, e => e.Action == AuditAction.SsoIdentityLinked);
    }

    [Fact]
    public async Task By_default_the_vault_wants_its_own_second_factor_even_if_the_idp_did_mfa()
    {
        var (service, _) = Build(VaultUser(), trustIdpMfa: false);

        var result = await service.SignInAsync(Identity(Email, true, "pwd", "otp"), CancellationToken.None);

        Assert.Equal(SsoOutcome.Linked, result.Outcome);
        Assert.False(result.SecondFactorSatisfied);
    }

    [Fact]
    public async Task An_operator_can_choose_to_trust_the_idps_second_factor()
    {
        var (service, _) = Build(VaultUser(), trustIdpMfa: true);

        var result = await service.SignInAsync(Identity(Email, true, "pwd", "otp"), CancellationToken.None);

        Assert.True(result.SecondFactorSatisfied);
    }

    [Fact]
    public async Task Trusting_the_idp_still_does_not_invent_a_factor_it_never_claimed()
    {
        // Even with trust switched on: no amr means the IdP asserted nothing, and silence is not
        // assurance. Most providers omit amr unless configured, so this is the common case.
        var (service, _) = Build(VaultUser(), trustIdpMfa: true);

        var result = await service.SignInAsync(Identity(Email, true), CancellationToken.None);

        Assert.Equal(SsoOutcome.Linked, result.Outcome);
        Assert.False(result.SecondFactorSatisfied);
    }

    [Fact]
    public async Task Every_refusal_is_recorded_against_the_subject_the_idp_named()
    {
        // A refusal nobody can see is a refusal nobody can investigate. There is no vault principal
        // yet, so the trail names who was turned away rather than filing it under "system".
        var (service, audit) = Build(user: null);

        await service.SignInAsync(Identity(), CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(Email, entry.ActorUsername);
        Assert.Contains("idp-subject-123", entry.Details);
    }
}
