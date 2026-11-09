using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Where a password, code or passkey response could leak after a Profile request: the audit trail,
/// the recorded notification emails and the vault's own log files, which every host in this test
/// process writes under its output folder.
/// </summary>
internal static class ProofDisclosure
{
    private static readonly string LogFolder = Path.Combine(AppContext.BaseDirectory, "logs");

    private static readonly DateTimeOffset ProcessStarted = new(Process.GetCurrentProcess().StartTime);

    /// <summary>
    /// Fails when any of <paramref name="secrets"/> appears in an audit row after <paramref name="sequence"/>,
    /// in an email recorded for the account or in a log line. <paramref name="loggedPath"/> must appear
    /// in the logs, which proves they were read and that the request was written to them.
    /// </summary>
    public static async Task AssertNotDisclosedAsync(
        VaultFixture vault, long sequence, Guid accountId, string loggedPath, params string[] secrets)
    {
        var rows = await AuditTrail.RowsAfterAsync(vault, sequence);
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var account = await db.Users.AsNoTracking().SingleAsync(user => user.Id == accountId);
        var emails = await db.EmailLogs.AsNoTracking().Where(email => email.ToAddress == account.Email).ToArrayAsync();
        var logs = await ReadLogsAsync();

        Assert.Contains(loggedPath, logs, StringComparison.Ordinal);
        Assert.All(secrets, secret =>
        {
            Assert.DoesNotContain(rows, row => ContainsSecret($"{row.Details} {row.ResourceName} {row.Username}", secret));
            Assert.DoesNotContain(emails, email => ContainsSecret($"{email.Subject} {email.Body} {email.Error}", secret));
            Assert.False(ContainsSecret(logs, secret), "A credential value appeared in application diagnostics.");
        });
    }

    public static async Task AssertMailNotRecordedAsync(VaultFixture vault, string address, params string[] secrets)
    {
        using var scope = vault.Services.CreateScope();
        var emails = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().EmailLogs.AsNoTracking()
            .Where(email => email.ToAddress == address).ToArrayAsync();
        Assert.All(secrets, secret => Assert.DoesNotContain(emails,
            email => ContainsSecret($"{email.Subject} {email.Body} {email.Error}", secret)));
    }

    public static string[] CredentialComponents(object credential)
        => JsonSerializer.SerializeToElement(credential).EnumerateObject().Select(field => field.Value.GetString()!).ToArray();

    internal static bool ContainsSecret(string text, string secret)
    {
        // A six-digit code can sit inside a trace identifier, or on one side of a logged decimal.
        if (secret.Length != 6 || !secret.All(char.IsAsciiDigit))
            return text.Contains(secret, StringComparison.Ordinal);

        return Regex.IsMatch(
            text,
            $@"(?<![A-Za-z0-9])(?<![0-9]\.){Regex.Escape(secret)}(?!\.[0-9])(?![A-Za-z0-9])");
    }

    internal static bool LogFileWasWrittenDuringThisRun(DateTime lastWriteUtc, DateTimeOffset started)
        => lastWriteUtc >= started.UtcDateTime;

    internal static string LogTextFromThisRun(string raw, DateTimeOffset started)
    {
        var contents = new List<string>();
        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
                continue;

            try
            {
                using var document = JsonDocument.Parse(line);
                if (WrittenBefore(document.RootElement, started))
                    continue;

                contents.Add(line);
                contents.AddRange(StringValues(document.RootElement));
            }
            catch (JsonException)
            {
                // A line that cannot be dated, such as one still being written, is checked as raw text.
                contents.Add(line);
            }
        }

        return string.Join('\n', contents);
    }

    private static async Task<string> ReadLogsAsync()
    {
        // Daily files stay for two weeks, and today's file keeps earlier runs.
        var contents = new List<string>();
        foreach (var file in Directory.EnumerateFiles(LogFolder, "*.clef"))
        {
            if (!LogFileWasWrittenDuringThisRun(File.GetLastWriteTimeUtc(file), ProcessStarted))
                continue;

            // The vault keeps writing while the test reads.
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var text = LogTextFromThisRun(await reader.ReadToEndAsync(), ProcessStarted);
            if (text.Length > 0)
                contents.Add(text);
        }

        return string.Join('\n', contents);
    }

    // Only a valid timestamp can prove a line belongs to an earlier run; anything else is still checked.
    private static bool WrittenBefore(JsonElement value, DateTimeOffset started)
        => value.TryGetProperty("@t", out var stamp)
            && stamp.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var written)
            && written < started;

    private static IEnumerable<string> StringValues(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => [value.GetString()!],
        JsonValueKind.Object => value.EnumerateObject().SelectMany(property => StringValues(property.Value)),
        JsonValueKind.Array => value.EnumerateArray().SelectMany(StringValues),
        _ => [],
    };

}
