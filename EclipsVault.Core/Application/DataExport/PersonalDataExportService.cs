using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.AccessRequests;
using EclipsVault.Core.Application.Activity;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Application.Mfa;
using EclipsVault.Core.Application.Profile;

namespace EclipsVault.Core.Application.DataExport;

/// <summary>
/// Read-model aggregator for the personal-data export. It composes the account services — each
/// keyed by the caller's own id — into a flat, metadata-only <see cref="PersonalDataExport"/>. By
/// construction it never touches secret ciphertext, key material, or any decrypting path: the only
/// dependencies are the same self-service read APIs the account pages already use.
/// </summary>
public sealed class PersonalDataExportService : IPersonalDataExportService
{
    /// <summary>Upper bound on how many recent activity rows the export carries, newest first.</summary>
    public const int MaxActivityEntries = 250;

    private readonly IProfileService _profile;
    private readonly IPasskeyService _passkeys;
    private readonly IMfaRecoveryService _recovery;
    private readonly ISignedInDevices _sessions;
    private readonly IAccessRequestService _accessRequests;
    private readonly IAuditLogReader _audit;

    public PersonalDataExportService(
        IProfileService profile,
        IPasskeyService passkeys,
        IMfaRecoveryService recovery,
        ISignedInDevices sessions,
        IAccessRequestService accessRequests,
        IAuditLogReader audit)
    {
        _profile = profile;
        _passkeys = passkeys;
        _recovery = recovery;
        _sessions = sessions;
        _accessRequests = accessRequests;
        _audit = audit;
    }

    public async Task<PersonalDataExport?> BuildForUserAsync(Guid userId, CancellationToken ct)
    {
        var profile = await _profile.GetAsync(userId, ct);
        if (profile is null)
        {
            return null;
        }

        var passkeys = await _passkeys.ListForUserAsync(userId, ct);
        var backupCodes = await _recovery.CountRemainingAsync(userId, ct);
        var devices = await _sessions.ListAsync(userId, ct);
        var requests = await _accessRequests.ListMineAsync(userId, ct);
        var activity = await _audit.ListForActorAsync(userId, 0, MaxActivityEntries, ct);

        var account = new ExportAccount(
            profile.Username,
            profile.DisplayName,
            profile.Email,
            profile.Clearance.ToString(),
            profile.ProjectKey,
            profile.HasCustomAvatar);

        var security = new ExportSecurity(
            profile.TotpEnabled,
            backupCodes,
            passkeys.Select(p => new ExportPasskey(p.Nickname, p.CreatedAtUtc)).ToList());

        var deviceList = devices
            .Select(d => new ExportDevice(d.Device, d.IpAddress, d.CreatedAtUtc, d.LastSeenAtUtc))
            .ToList();

        var requestList = requests
            .Select(r => new ExportAccessRequest(
                r.SecretName, r.ProjectKey, r.Status.ToString(), r.Reason, r.CreatedAtUtc, r.DecidedAtUtc, r.DecidedBy))
            .ToList();

        var activityList = activity
            .Select(a => new ExportActivityEntry(
                a.TimestampUtc, ActivityDescriber.Describe(a.Action).Title, a.ResourceName, a.SourceIp))
            .ToList();

        return new PersonalDataExport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            SchemaVersion: PersonalDataExport.CurrentSchemaVersion,
            Account: account,
            Security: security,
            SignedInDevices: deviceList,
            AccessRequests: requestList,
            RecentActivity: activityList,
            Notice: PersonalDataExport.StandardNotice);
    }
}
