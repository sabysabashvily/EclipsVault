using System.Text.Json;

namespace EclipsVault.Core.Application.Auditing;

/// <summary>
/// An export that is already recorded in the trail. Its entries are read only as
/// <see cref="WriteToAsync"/> writes them, so memory stays the same however long the trail is.
/// The document written is the one an <see cref="AuditBundle"/> serializes to, so every verifier
/// that reads its format reads it. Disposing it lets the next export start.
/// </summary>
public sealed class AuditBundleExport(
    DateTimeOffset exportedAtUtc,
    byte[] publicKeySpki,
    AuditBundleCheckpoint checkpoint,
    IAsyncEnumerable<AuditBundleRow> rows,
    IAsyncDisposable turn) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public AuditBundleCheckpoint Checkpoint => checkpoint;

    public string FileName => $"eclipsvault-audit-bundle-{exportedAtUtc:yyyyMMdd-HHmmss}.json";

    public Task WriteToAsync(Stream destination, CancellationToken ct)
        => JsonSerializer.SerializeAsync(
            destination, new Document(AuditBundleSchema.Current, exportedAtUtc, publicKeySpki, checkpoint, rows), Indented, ct);

    public ValueTask DisposeAsync() => turn.DisposeAsync();

    // AuditBundle's members, in its order, with rows the serializer writes as it reads them.
    private sealed record Document(
        string SchemaVersion,
        DateTimeOffset ExportedAtUtc,
        byte[] PublicKeySpki,
        AuditBundleCheckpoint Checkpoint,
        IAsyncEnumerable<AuditBundleRow> Rows);
}
