using System.Text;
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
    public async Task The_export_writes_the_indented_schema_v2_document()
    {
        AuditBundleRow[] rows = [Row(1), Row(2)];
        await using var export = new AuditBundleExport(ExportedAt, PublicKey, Checkpoint, rows.ToAsyncEnumerable(), new NoTurn());

        using var written = new MemoryStream();
        await export.WriteToAsync(written, default);

        Assert.Equal("""
            {
              "SchemaVersion": "eclipsvault.audit-bundle/2",
              "ExportedAtUtc": "2026-11-13T12:00:00+00:00",
              "PublicKeySpki": "AQID",
              "Checkpoint": {
                "Sequence": 2,
                "ChainHeadHash": "hash-2",
                "CreatedAtUtc": "2026-11-13T12:00:00+00:00",
                "SigningKeyId": "key-1",
                "Signature": "BAUG"
              },
              "Rows": [
                {
                  "Sequence": 1,
                  "Id": "00000000-0000-0000-0000-000000000000",
                  "TimestampUtc": "2026-11-13T12:00:00+00:00",
                  "UserId": null,
                  "Username": "alice",
                  "SourceIp": "203.0.113.5",
                  "Action": 1,
                  "ResourceType": "Secret",
                  "ResourceId": null,
                  "ResourceName": "db-password",
                  "Details": "Revealed a secret",
                  "IsCritical": false,
                  "PreviousHash": "hash-0",
                  "EntryHash": "hash-1",
                  "HashVersion": 2
                },
                {
                  "Sequence": 2,
                  "Id": "00000000-0000-0000-0000-000000000000",
                  "TimestampUtc": "2026-11-13T12:00:00+00:00",
                  "UserId": null,
                  "Username": "alice",
                  "SourceIp": "203.0.113.5",
                  "Action": 1,
                  "ResourceType": "Secret",
                  "ResourceId": null,
                  "ResourceName": "db-password",
                  "Details": "Revealed a secret",
                  "IsCritical": false,
                  "PreviousHash": "hash-1",
                  "EntryHash": "hash-2",
                  "HashVersion": 2
                }
              ]
            }
            """, Encoding.UTF8.GetString(written.ToArray()));
    }

    [Fact]
    public async Task Entries_reach_the_destination_before_the_last_one_is_read()
    {
        using var destination = new MemoryStream();
        var writtenBeforeTheLast = 0L;
        // A single row exceeds the serializer's default 16 KiB buffer and forces a write.
        const int payloadLargerThanSerializerBuffer = 64 * 1024;
        var oversizedRow = Row(1) with { Details = new string('x', payloadLargerThanSerializerBuffer) };

        async IAsyncEnumerable<AuditBundleRow> Trail()
        {
            yield return oversizedRow;
            await Task.Yield();
            writtenBeforeTheLast = destination.Length;
            yield return Row(2);
        }

        await using var export = new AuditBundleExport(ExportedAt, PublicKey, Checkpoint, Trail(), new NoTurn());
        await export.WriteToAsync(destination, default);

        Assert.True(writtenBeforeTheLast > 0, "The whole trail was read before anything was written.");
    }

    private static AuditBundleRow Row(int sequence) => new(
        sequence, Guid.Empty, ExportedAt, null, "alice", "203.0.113.5", 1, "Secret", null, "db-password",
        "Revealed a secret", false, $"hash-{sequence - 1}", $"hash-{sequence}", AuditRowHasher.CurrentVersion);
}
