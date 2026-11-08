namespace EclipsVault.Web.Models.Ui;

public sealed record NetworksResponse(string CurrentAddress, bool CurrentAddressTrusted, IReadOnlyList<string> ConfiguredRanges,
    IReadOnlyList<TrustedNetworkResponse> TrustedNetworks, IReadOnlyList<NetworkBlockResponse> Blocks);

public sealed record TrustedNetworkResponse(Guid NetworkId, string Range, string Label, string AddedBy, DateTimeOffset AddedAtUtc);

public sealed record NetworkBlockResponse(string Range, string Reason, DateTimeOffset BlockedAtUtc);

public sealed record TrustedRangeResponse(string Range);

public sealed class AddNetworkRequest
{
    public string? Range { get; init; }
    public string? Label { get; init; }
}

public sealed class LiftBlockRequest
{
    public string? Range { get; init; }
}
