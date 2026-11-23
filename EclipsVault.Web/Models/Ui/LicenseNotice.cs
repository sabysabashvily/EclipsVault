namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// An advisory the Angular UI shows to administrators who can act on the licence.
/// </summary>
/// <param name="Title">The lead-in, e.g. "Running unlicensed."</param>
/// <param name="Text">The rest of the notice.</param>
public sealed record LicenseNotice(string Title, string Text);
