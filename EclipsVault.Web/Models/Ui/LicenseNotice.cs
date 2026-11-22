namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// The licensing reminder the Legacy UI's banner showed, worded by the server as it was.
/// Soft: it changes nothing about how the vault runs.
/// </summary>
/// <param name="Title">The lead-in, e.g. "Running unlicensed."</param>
/// <param name="Text">The rest of the notice.</param>
public sealed record LicenseNotice(string Title, string Text);
