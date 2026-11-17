using System.Globalization;
using System.Security.Claims;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.DynamicSecrets;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Application.StepUp;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Tests.TestDoubles;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Controllers;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EclipsVault.Tests.Web;

/// <summary>
/// The Legacy Dynamic secrets page over the real lease service and a fake target: what it tells the
/// person, what it records and what it logs when issuing or revoking goes wrong.
/// </summary>
public sealed class DynamicSecretsPageTests
{
    private static readonly Guid Holder = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Actor : IAuditContext
    {
        public Guid? UserId => Holder;
        public string? Username => "holder";
        public string? SourceIp => "::1";
    }

    private sealed class RecordingAuditSink : IAuditSink
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class AccessPolicy(params string[] denialReasons) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => throw new NotSupportedException();

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(denialReasons.Length == 0
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed(AuthorizationFailure.Failed(
                    denialReasons.Select(reason => new AuthorizationFailureReason(new PassThroughAuthorizationHandler(), reason)))));
    }

    private sealed class Unlicensed : ILicenseState
    {
        public LicenseStatus Status => LicenseStatus.Missing;
        public LicenseClaims? Claims => null;
        public string Message => string.Empty;
        public bool Allows(string feature) => false;
    }

    private sealed class EvaluationStore(bool readFails = false) : IEvaluationWindowStore
    {
        public Task<DateTimeOffset?> PeekAsync(string featureKey, CancellationToken ct)
            => readFails ? throw new InvalidOperationException("The licence store is unavailable.") : Task.FromResult<DateTimeOffset?>(null);

        public Task<EvaluationStart> GetOrStartAsync(string featureKey, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class PageLog : ILogger<DynamicSecretsController>
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, $"{formatter(state, exception)} {exception}"));
    }

    private sealed class NoTempDataStore : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static readonly IStepUpService StepUp = new StepUpService(
        new StepUpOptions { MinimumSensitivity = SensitivityLevel.Secret, MaxAuthAgeMinutes = 10 },
        users: null!, secondFactors: null!, attempt: null!, failures: null!, clock: null!, revocation: null!);

    private static DynamicSecretRole Role(SensitivityLevel sensitivity = SensitivityLevel.Internal) => new()
    {
        Id = Guid.NewGuid(),
        Name = "reader",
        ProjectKey = "GLOBAL",
        Sensitivity = sensitivity,
        Backend = DynamicSecretBackend.SqlServer,
        DefaultTtlMinutes = 15,
        MaxTtlMinutes = 60,
        IsEnabled = true,
    };

    private static DynamicSecretService Service(
        FakeLeaseRepository repository, FakeDynamicTarget target, IAuditSink? audit = null, IPremiumFeatureUsage? licence = null)
        => new(repository, [target], new Actor(), TimeProvider.System, licence ?? new RecordingPremiumFeatureUsage(),
            audit ?? new RecordingAuditSink());

    private static DynamicSecretsController Page(
        IDynamicSecretService service, PageLog log, IAuthorizationService? access = null,
        IEvaluationWindowStore? evaluations = null, int minutesSinceSignIn = 1)
    {
        var signedIn = DateTimeOffset.UtcNow.AddMinutes(-minutesSinceSignIn).ToUnixTimeSeconds();
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Holder.ToString()),
            new Claim(ClaimTypes.Name, "holder"),
            new Claim(VaultClaimTypes.AuthTime, signedIn.ToString(CultureInfo.InvariantCulture)),
        ], "test");
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new DynamicSecretsController(service, access ?? new AccessPolicy(), new Unlicensed(),
            evaluations ?? new EvaluationStore(), StepUp, TimeProvider.System, log)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoTempDataStore()),
        };
    }

    [Fact]
    public async Task A_failed_evaluation_read_serves_the_page_without_its_licence_note()
    {
        var log = new PageLog();
        var page = Page(Service(new FakeLeaseRepository(Role()), new FakeDynamicTarget()), log,
            evaluations: new EvaluationStore(readFails: true));

        var view = Assert.IsType<ViewResult>(await page.Index(CancellationToken.None));

        Assert.True(Assert.IsType<DynamicSecretsViewModel>(view.Model).LicenceNoteUnavailable);
        Assert.Equal(LogLevel.Warning, Assert.Single(log.Entries).Level);
    }

    [Theory]
    [InlineData(SensitivityLevel.Internal, 11, true)]
    [InlineData(SensitivityLevel.Confidential, 11, true)]
    [InlineData(SensitivityLevel.Secret, 11, false)]
    [InlineData(SensitivityLevel.TopSecret, 11, false)]
    [InlineData(SensitivityLevel.Secret, 9, true)]
    [InlineData(SensitivityLevel.TopSecret, 9, true)]
    public async Task A_role_at_or_above_secret_issues_only_within_ten_minutes_of_a_sign_in(
        SensitivityLevel sensitivity, int minutesSinceSignIn, bool issued)
    {
        var role = Role(sensitivity);
        var repository = new FakeLeaseRepository(role);
        var audit = new RecordingAuditSink();
        var licence = new RecordingPremiumFeatureUsage();
        var page = Page(Service(repository, new FakeDynamicTarget(), audit, licence), new PageLog(), minutesSinceSignIn: minutesSinceSignIn);

        await page.Issue(new IssueCredentialViewModel { RoleId = role.Id }, CancellationToken.None);

        Assert.Equal(issued, repository.Leases.Count == 1);
        Assert.Equal(issued, licence.Recorded.Count == 1); // a refusal never reaches the licence gate
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task A_stale_session_is_asked_to_sign_in_again_before_issuing_a_secret_role()
    {
        var role = Role(SensitivityLevel.Secret);
        var page = Page(Service(new FakeLeaseRepository(role), new FakeDynamicTarget()), new PageLog(), minutesSinceSignIn: 11);

        await page.Issue(new IssueCredentialViewModel { RoleId = role.Id }, CancellationToken.None);

        Assert.Equal("Confirm it's you before issuing 'reader': sign in again, then retry.", page.TempData[FlashExtensions.MessageKey]);
    }

    [Fact]
    public async Task A_refused_role_records_the_denial_with_the_policys_reasons_and_mints_nothing()
    {
        var role = Role();
        var target = new FakeDynamicTarget();
        var audit = new RecordingAuditSink();
        var licence = new RecordingPremiumFeatureUsage();
        var page = Page(Service(new FakeLeaseRepository(role), target, audit, licence), new PageLog(),
            access: new AccessPolicy("Clearance too low."));

        await page.Issue(new IssueCredentialViewModel { RoleId = role.Id }, CancellationToken.None);

        var denial = Assert.Single(audit.Entries);
        Assert.Equal(AuditAction.SecretAccessDenied, denial.Action);
        Assert.Equal("Clearance too low.", denial.Details);
        Assert.Empty(target.Passwords);
        Assert.Empty(licence.Recorded);
        Assert.Equal("Your clearance and project do not permit issuing 'reader'.", page.TempData[FlashExtensions.MessageKey]);
    }

    [Fact]
    public async Task A_mint_whose_cleanup_fails_says_so_and_logs_role_and_login_at_critical_without_the_password()
    {
        var role = Role();
        var target = new FakeDynamicTarget
        {
            StatementsThatRun = 1,
            MintFailure = new InvalidOperationException("Cannot create the user."),
            RevokeFailure = new InvalidOperationException("Cannot drop the login."),
        };
        var log = new PageLog();
        var page = Page(Service(new FakeLeaseRepository(role), target), log);

        await page.Issue(new IssueCredentialViewModel { RoleId = role.Id }, CancellationToken.None);

        Assert.Equal(
            "The backend failed part-way through, and the vault could not confirm that the login it started to create was removed. " +
            "No password was shown or kept. An administrator must check the target.",
            page.TempData[FlashExtensions.MessageKey]);
        var alert = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Critical);
        Assert.Contains("reader", alert.Text);
        Assert.Contains(Assert.Single(target.Logins), alert.Text);
        Assert.DoesNotContain(log.Entries, entry => entry.Text.Contains(Assert.Single(target.Passwords)));
    }

    [Fact]
    public async Task An_unrecorded_credential_whose_removal_fails_logs_role_and_login_at_critical_without_the_password()
    {
        var role = Role();
        var target = new FakeDynamicTarget { RevokeFailure = new InvalidOperationException("Cannot drop the login.") };
        var log = new PageLog();
        var page = Page(Service(new FakeLeaseRepository(role) { AddFails = true }, target), log);

        await Assert.ThrowsAsync<MintUnrecordedException>(
            () => page.Issue(new IssueCredentialViewModel { RoleId = role.Id }, CancellationToken.None));

        var alert = Assert.Single(log.Entries, entry => entry.Level == LogLevel.Critical);
        Assert.Contains("reader", alert.Text);
        Assert.Contains(Assert.Single(target.Logins), alert.Text);
        Assert.DoesNotContain(log.Entries, entry => entry.Text.Contains(Assert.Single(target.Passwords)));
    }

    [Fact]
    public async Task A_refused_revoke_says_the_credential_may_still_work()
    {
        var role = Role();
        var target = new FakeDynamicTarget();
        var service = Service(new FakeLeaseRepository(role), target);
        var issued = await service.IssueAsync(role.Id, null, CancellationToken.None);
        target.RevokeFailure = new InvalidOperationException("Cannot drop the login.");
        var page = Page(service, new PageLog());

        await page.Revoke(issued.LeaseId, CancellationToken.None);

        Assert.Equal("The backend refused to destroy the credential; it may still work.", page.TempData[FlashExtensions.MessageKey]);
    }

    [Fact]
    public async Task A_revoke_whose_role_is_gone_says_why_the_credential_was_not_destroyed()
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var service = Service(repository, new FakeDynamicTarget());
        var issued = await service.IssueAsync(role.Id, null, CancellationToken.None);
        repository.RoleRemoved = true;
        var page = Page(service, new PageLog());

        await page.Revoke(issued.LeaseId, CancellationToken.None);

        Assert.Equal(
            "The role that minted this credential no longer exists, so it could not be destroyed automatically.",
            page.TempData[FlashExtensions.MessageKey]);
    }

    [Fact]
    public async Task A_revoke_whose_close_cannot_be_recorded_says_the_credential_was_destroyed()
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var service = Service(repository, new FakeDynamicTarget());
        var issued = await service.IssueAsync(role.Id, null, CancellationToken.None);
        repository.UpdatesToFail = 1;
        var page = Page(service, new PageLog());

        await page.Revoke(issued.LeaseId, CancellationToken.None);

        Assert.Equal(
            "The credential was destroyed, but the vault couldn't record it in the audit trail. Its lease shows Active until it expires.",
            page.TempData[FlashExtensions.MessageKey]);
    }
}
