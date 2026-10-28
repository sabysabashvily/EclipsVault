using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.Profile;

public class AccountCredentialsTests
{
    private static readonly Guid OnlyPasskey = Guid.NewGuid();
    private static readonly Guid OtherPasskey = Guid.NewGuid();

    [Fact]
    public void An_enrolled_authenticator_with_unused_codes_and_a_passkey_offers_every_method()
    {
        var credentials = new AccountCredentials(AuthenticatorEnrolled: true, UnusedRecoveryCodes: 3, PasskeyIds: [OnlyPasskey]);

        Assert.Equal(
            [AccountProofMethod.PasswordAndAuthenticatorCode, AccountProofMethod.PasswordAndRecoveryCode, AccountProofMethod.Passkey],
            credentials.MethodsFor(ProfileOperationKind.ChangePassword, null));
    }

    [Fact]
    public void Recovery_codes_offer_nothing_without_an_enrolled_authenticator()
    {
        var credentials = new AccountCredentials(AuthenticatorEnrolled: false, UnusedRecoveryCodes: 10, PasskeyIds: [OnlyPasskey]);

        Assert.Equal([AccountProofMethod.Passkey], credentials.MethodsFor(ProfileOperationKind.AddPasskey, null));
    }

    [Fact]
    public void A_passkey_cannot_verify_its_own_removal()
    {
        var credentials = new AccountCredentials(AuthenticatorEnrolled: true, UnusedRecoveryCodes: 0, PasskeyIds: [OnlyPasskey]);

        Assert.Equal([AccountProofMethod.PasswordAndAuthenticatorCode], credentials.MethodsFor(ProfileOperationKind.RemovePasskey, OnlyPasskey));
    }

    [Fact]
    public void Another_passkey_can_verify_removing_one()
    {
        var credentials = new AccountCredentials(AuthenticatorEnrolled: false, UnusedRecoveryCodes: 0, PasskeyIds: [OnlyPasskey, OtherPasskey]);

        Assert.Equal([AccountProofMethod.Passkey], credentials.MethodsFor(ProfileOperationKind.RemovePasskey, OnlyPasskey));
        Assert.Null(credentials.RefusalFor(ProfileOperationKind.RemovePasskey, OnlyPasskey));
    }

    [Fact]
    public void Removing_the_last_passkey_without_an_authenticator_needs_another_method()
    {
        var credentials = new AccountCredentials(AuthenticatorEnrolled: false, UnusedRecoveryCodes: 0, PasskeyIds: [OnlyPasskey]);

        Assert.Equal(ProfileRefusal.AlternateMethodRequired, credentials.RefusalFor(ProfileOperationKind.RemovePasskey, OnlyPasskey));
    }

    [Fact]
    public void Removing_a_passkey_the_account_does_not_have_is_refused_as_not_found()
    {
        var credentials = new AccountCredentials(AuthenticatorEnrolled: true, UnusedRecoveryCodes: 0, PasskeyIds: [OnlyPasskey]);

        Assert.Equal(ProfileRefusal.PasskeyNotFound, credentials.RefusalFor(ProfileOperationKind.RemovePasskey, OtherPasskey));
    }

    [Theory]
    [InlineData(ProfileOperationKind.ResetAuthenticator)]
    [InlineData(ProfileOperationKind.ReplaceRecoveryCodes)]
    public void Authenticator_changes_need_an_enrolled_authenticator(ProfileOperationKind kind)
    {
        var credentials = new AccountCredentials(AuthenticatorEnrolled: false, UnusedRecoveryCodes: 0, PasskeyIds: [OnlyPasskey]);

        Assert.Equal(ProfileRefusal.AuthenticatorNotEnrolled, credentials.RefusalFor(kind, null));
    }

    [Fact]
    public void An_account_with_no_local_credential_can_verify_nothing()
    {
        var credentials = new AccountCredentials(AuthenticatorEnrolled: false, UnusedRecoveryCodes: 0, PasskeyIds: []);

        Assert.Equal(ProfileRefusal.MethodUnavailable, credentials.RefusalFor(ProfileOperationKind.ChangePassword, null));
    }

    [Fact]
    public void A_spent_recovery_code_keeps_working_only_while_another_unused_code_remains()
    {
        var lastCodeSpent = new AccountCredentials(AuthenticatorEnrolled: true, UnusedRecoveryCodes: 0, PasskeyIds: []);
        var codesLeft = new AccountCredentials(AuthenticatorEnrolled: true, UnusedRecoveryCodes: 1, PasskeyIds: []);

        Assert.False(lastCodeSpent.KeepsWorkingWithoutPasskeys(AccountProofMethod.PasswordAndRecoveryCode));
        Assert.True(codesLeft.KeepsWorkingWithoutPasskeys(AccountProofMethod.PasswordAndRecoveryCode));
    }

    [Fact]
    public void An_authenticator_code_keeps_working_only_while_the_authenticator_stays_enrolled()
    {
        var reset = new AccountCredentials(AuthenticatorEnrolled: false, UnusedRecoveryCodes: 0, PasskeyIds: []);
        var enrolled = new AccountCredentials(AuthenticatorEnrolled: true, UnusedRecoveryCodes: 0, PasskeyIds: []);

        Assert.False(reset.KeepsWorkingWithoutPasskeys(AccountProofMethod.PasswordAndAuthenticatorCode));
        Assert.True(enrolled.KeepsWorkingWithoutPasskeys(AccountProofMethod.PasswordAndAuthenticatorCode));
    }
}
