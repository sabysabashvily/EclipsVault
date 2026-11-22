using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Web.Models.Ui;

namespace EclipsVault.Web.Services;

/// <summary>
/// What a managed secret's page says about its licence. It only peeks at the evaluation window, so viewing
/// the page never starts one, and it fails open: an unreadable window leaves the note out, never the page.
/// </summary>
public static class ManagedRotationNote
{
    public static async Task<ManagedSecretResponse> ReadAsync(
        string principal, ILicenseState license, IEvaluationWindowStore evaluations, TimeProvider clock, ILogger logger, CancellationToken ct)
    {
        if (license.Allows(LicenseFeatures.ManagedRotation))
        {
            return new ManagedSecretResponse(principal, Licensed: true, EvaluationKnown: true, EvaluationDaysRemaining: null);
        }

        try
        {
            var days = await evaluations.DaysRemainingAsync(LicenseFeatures.ManagedRotation, clock, ct);
            return new ManagedSecretResponse(principal, Licensed: false, EvaluationKnown: true, days);
        }
        catch (Exception unreadable) when (unreadable is not OperationCanceledException)
        {
            logger.LogWarning(unreadable, "The managed rotation evaluation could not be read; its note is left out");
            return new ManagedSecretResponse(principal, Licensed: false, EvaluationKnown: false, EvaluationDaysRemaining: null);
        }
    }
}
