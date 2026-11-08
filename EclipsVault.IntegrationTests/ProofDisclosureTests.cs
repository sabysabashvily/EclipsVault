using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class ProofDisclosureTests
{
    private static readonly DateTimeOffset RunStarted = new(2026, 11, 8, 12, 0, 1, TimeSpan.Zero);

    [Theory]
    [InlineData("{\"@sp\":\"0b01123456362157\"}", "123456", false)]
    [InlineData("{\"code\":\"123456\"}", "123456", true)]
    [InlineData("The submitted code was 123456.", "123456", true)]
    [InlineData("before-Mailbox-Code-Disclosure-Sentinel-after", "Mailbox-Code-Disclosure-Sentinel", true)]
    public void Disclosure_checks_match_credential_values_without_mistaking_trace_digits_for_a_code(string text, string secret, bool disclosed)
        => Assert.Equal(disclosed, ProofDisclosure.ContainsSecret(text, secret));

    [Theory]
    [InlineData("{\"Elapsed\":6.145708}", "145708", false)]
    [InlineData("145708.5", "145708", false)]
    [InlineData("145708.50", "145708", false)]
    [InlineData("The submitted code was 145708.", "145708", true)]
    [InlineData("6.145708 and the code 145708", "145708", true)]
    public void A_six_digit_code_inside_a_decimal_number_is_not_a_disclosure(string text, string secret, bool disclosed)
        => Assert.Equal(disclosed, ProofDisclosure.ContainsSecret(text, secret));

    [Theory]
    [InlineData(11, 0, 0, false)]
    [InlineData(12, 0, 1, true)]
    [InlineData(12, 0, 2, true)]
    public void A_clef_file_from_before_this_process_is_not_read(int hour, int minute, int second, bool writtenThisRun)
    {
        var lastWrite = new DateTime(2026, 11, 8, hour, minute, second, DateTimeKind.Utc);
        Assert.Equal(writtenThisRun, ProofDisclosure.LogFileWasWrittenDuringThisRun(lastWrite, RunStarted));
    }

    [Fact]
    public void Lines_written_before_this_process_are_left_out_of_the_log_text()
    {
        var raw =
            "{\"@t\":\"2026-11-08T11:00:00.0000000Z\",\"Elapsed\":6.111111}\n" +
            "{\"@t\":\"2026-11-08T12:00:01.0000000Z\",\"path\":\"/on-the-boundary\"}\n" +
            "{\"@t\":\"2026-11-08T12:00:02.0000000Z\",\"Elapsed\":6.222222}\n";

        var text = ProofDisclosure.LogTextFromThisRun(raw, RunStarted);

        Assert.DoesNotContain("111111", text, StringComparison.Ordinal);
        Assert.Contains("/on-the-boundary", text, StringComparison.Ordinal);
        Assert.Contains("\"Elapsed\":6.222222", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unfinished_final_log_line_from_this_run_is_still_checked()
    {
        var raw = "{\"@t\":\"2026-11-08T11:00:00.0000000Z\"}\n{\"path\":\"/api/ui/prof";

        var text = ProofDisclosure.LogTextFromThisRun(raw, RunStarted);

        Assert.DoesNotContain("11:00:00", text, StringComparison.Ordinal);
        Assert.Contains("/api/ui/prof", text, StringComparison.Ordinal);
    }
}
