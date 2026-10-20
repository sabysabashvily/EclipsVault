namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// The Your data page: the account and how many of each thing the export holds. Only counts reach
/// the page, never the rows behind them; the rows are in the export file.
/// </summary>
public sealed record DataResponse(
    DataAccountResponse Account,
    bool TwoStepEnabled,
    int PasskeyCount,
    int BackupCodesRemaining,
    int DeviceCount,
    int AccessRequestCount,
    int ActivityCount,
    string Notice);
