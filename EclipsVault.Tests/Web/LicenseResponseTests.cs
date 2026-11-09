using System.Security.Cryptography;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Controllers.Ui;
using EclipsVault.Web.Models.Ui;
using EclipsVault.Web.Services;
using Xunit;

namespace EclipsVault.Tests.Web;

public sealed class LicenseResponseTests
{
    private static readonly DateTimeOffset UpdateEnd = DateTimeOffset.Parse("2025-01-02T03:04:00Z");
    private static readonly LicenseClaims Perpetual = new("lic-perpetual", LicenseTier.Max, "Example", "contact@example.test",
        DateTimeOffset.Parse("2020-01-01T00:00:00Z"), null, UpdateEnd, 0, []);

    [Theory]
    [InlineData(LicenseStatus.Missing, "Missing")]
    [InlineData(LicenseStatus.Malformed, "Malformed")]
    [InlineData(LicenseStatus.InvalidSignature, "InvalidSignature")]
    [InlineData(LicenseStatus.Expired, "Expired")]
    [InlineData(LicenseStatus.Valid, "Valid")]
    public void Every_startup_status_is_reported_without_starting_an_evaluation(LicenseStatus status, string expected)
    {
        var state = new Snapshot(status, null, "Startup message");
        var response = new LicenseController(state, new(status, "Startup message", ["sso"]), new FixedClock(UpdateEnd)).Get().Value;

        Assert.Equal((expected, "Startup message", false), (response!.Status, response.Message, response.UpdatesLapsed));
        Assert.Null(response.Licence);
        Assert.Equal(["sso"], response.BeyondTier);
    }

    [Theory]
    [InlineData("2025-01-02T03:03:59.9999999Z", false)]
    [InlineData("2025-01-02T03:04:00Z", false)]
    [InlineData("2025-01-02T03:04:00.0000001Z", true)]
    public void The_update_window_lapses_only_after_its_last_instant(string now, bool expected)
    {
        var state = new Snapshot(LicenseStatus.Valid, Perpetual, "Licensed");
        var response = new LicenseController(state, new(LicenseStatus.Valid, "Licensed", []), new FixedClock(DateTimeOffset.Parse(now))).Get().Value;

        Assert.Equal(expected, response!.UpdatesLapsed);
        Assert.Equal("Valid", response.Status);
    }

    [Fact]
    public void A_signed_perpetual_licence_stays_valid_past_every_date()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var token = LicenseSigner.Sign(Perpetual, key);
        var clock = new FixedClock(DateTimeOffset.Parse("2100-01-01T00:00:00Z"));
        var verified = LicenseVerifier.Verify(token, key.ExportSubjectPublicKeyInfo(), clock.GetUtcNow());
        var state = new Snapshot(verified.Status, verified.Claims, verified.Message);

        var response = new LicenseController(state, new(verified.Status, verified.Message, []), clock).Get().Value;

        Assert.Equal("Valid", response!.Status);
        Assert.Equal("Licensed to Example (Max).", response.Message);
        Assert.True(response.UpdatesLapsed);
        Assert.Equal(new LicenseDetailsResponse("Example", "Max", "contact@example.test", "lic-perpetual",
            DateTimeOffset.Parse("2020-01-01T00:00:00Z"), null, UpdateEnd, 0), response.Licence);
    }

    [Fact]
    public void A_time_limited_expired_licence_keeps_its_trusted_details()
    {
        var expired = Perpetual with { NotAfterUtc = UpdateEnd };
        var state = new Snapshot(LicenseStatus.Expired, expired, "The license expired on 2025-01-02.");

        var response = new LicenseController(state, new(LicenseStatus.Expired, state.Message, []), new FixedClock(UpdateEnd.AddYears(1))).Get().Value;

        Assert.Equal("Expired", response!.Status);
        Assert.Equal(UpdateEnd, response.Licence!.ExpiresAtUtc);
        Assert.Equal("Example", response.Licence.IssuedTo);
    }

    private sealed record Snapshot(LicenseStatus Status, LicenseClaims? Claims, string Message) : ILicenseState
    {
        public bool Allows(string feature) => throw new InvalidOperationException("Reading License must not evaluate capabilities.");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
