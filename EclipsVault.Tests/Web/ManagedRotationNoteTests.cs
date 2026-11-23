using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Models.Ui;
using EclipsVault.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.Tests.Web;

public sealed class ManagedRotationNoteTests
{
    [Fact]
    public async Task An_unreadable_evaluation_leaves_the_note_out_and_the_page_is_still_answered()
    {
        var windows = new FakeWindows { Fail = new InvalidOperationException("The evaluation store is down.") };

        var note = await ManagedRotationNote.ReadAsync("app_login", new FakeLicense(allows: false), windows, TimeProvider.System, NullLogger.Instance, default);

        Assert.Equal(new ManagedSecretResponse("app_login", Licensed: false, EvaluationKnown: false, EvaluationDaysRemaining: null), note);
        Assert.Equal(0, windows.TimesStarted);
    }

    [Fact]
    public async Task An_unstarted_evaluation_is_reported_without_starting_it()
    {
        var windows = new FakeWindows();

        var note = await ManagedRotationNote.ReadAsync("app_login", new FakeLicense(allows: false), windows, TimeProvider.System, NullLogger.Instance, default);

        Assert.Equal(new ManagedSecretResponse("app_login", Licensed: false, EvaluationKnown: true, EvaluationDaysRemaining: null), note);
        Assert.Equal(0, windows.TimesStarted);
    }

    [Fact]
    public async Task A_licensed_vault_reads_no_evaluation()
    {
        var windows = new FakeWindows { Fail = new InvalidOperationException("Read when it should not be.") };

        var note = await ManagedRotationNote.ReadAsync("app_login", new FakeLicense(allows: true), windows, TimeProvider.System, NullLogger.Instance, default);

        Assert.Equal(new ManagedSecretResponse("app_login", Licensed: true, EvaluationKnown: true, EvaluationDaysRemaining: null), note);
    }

    private sealed class FakeLicense(bool allows) : ILicenseState
    {
        public LicenseStatus Status => allows ? LicenseStatus.Valid : LicenseStatus.Missing;

        public LicenseClaims? Claims => null;

        public string Message => "";

        public bool Allows(string feature) => allows;
    }

    /// <summary>A window store that has never been started, or that fails every read when <see cref="Fail"/> is set.</summary>
    private sealed class FakeWindows : IEvaluationWindowStore
    {
        public Exception? Fail { get; init; }

        public int TimesStarted { get; private set; }

        public Task<DateTimeOffset?> PeekAsync(string featureKey, CancellationToken ct)
            => Fail is null ? Task.FromResult<DateTimeOffset?>(null) : throw Fail;

        public Task<EvaluationStart> GetOrStartAsync(string featureKey, CancellationToken ct)
        {
            TimesStarted++;
            return Task.FromResult(new EvaluationStart(DateTimeOffset.UtcNow, StartedNow: true));
        }
    }
}
