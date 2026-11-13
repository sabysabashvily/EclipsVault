using System.Text;
using System.Text.Json;
using EclipsVault.Core.Application.Auditing;
using Xunit;

namespace EclipsVault.Tests.Auditing;

public class AuditBundleExportTests
{
    private static readonly DateTimeOffset ExportedAt = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] PublicKey = [1, 2, 3];

    private static readonly AuditBundleCheckpoint Checkpoint = new(2, "hash-2", ExportedAt, "key-1", [4, 5, 6]);

    private sealed class NoTurn : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Written_out_it_is_the_document_an_audit_bundle_serializes_to()
    {
        AuditBundleRow[] rows = [Row(1), Row(2)];
        var bundle = new AuditBundle(AuditBundleSchema.V2, ExportedAt, PublicKey, Checkpoint, rows);
        await using var export = new AuditBundleExport(ExportedAt, PublicKey, Checkpoint, rows.ToAsyncEnumerable(), new NoTurn());

        using var written = new MemoryStream();
        await export.WriteToAsync(written, default);

        Assert.Equal(JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8.GetString(written.ToArray()));
    }

    [Fact]
    public async Task Entries_reach_the_destination_before_the_last_one_is_read()
    {
        using var destination = new MemoryStream();
        var writtenBeforeTheLast = 0L;

        async IAsyncEnumerable<AuditBundleRow> Trail()
        {
            foreach (var sequence in Enumerable.Range(1, 999))
            {
                yield return Row(sequence);
            }

            await Task.Yield();
            writtenBeforeTheLast = destination.Length;
            yield return Row(1000);
        }

        await using var export = new AuditBundleExport(ExportedAt, PublicKey, Checkpoint, Trail(), new NoTurn());
        await export.WriteToAsync(destination, default);

        Assert.True(writtenBeforeTheLast > 0, "The whole trail was read before anything was written.");
    }

    private static AuditBundleRow Row(int sequence) => new(
        sequence, Guid.Empty, ExportedAt, null, "alice", "203.0.113.5", 1, "Secret", null, "db-password",
        "Revealed a secret", false, $"hash-{sequence - 1}", $"hash-{sequence}", AuditRowHasher.CurrentVersion);
}
