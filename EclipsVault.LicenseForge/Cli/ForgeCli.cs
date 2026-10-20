using EclipsVault.LicenseForge.Commands;
using EclipsVault.LicenseForge.Rendering;

namespace EclipsVault.LicenseForge.Cli;

/// <summary>
/// The command-line host: resolves whether output should be themed, then routes the first argument to
/// a verb. Kept deliberately thin — one place knows the verb table and the exit-code contract, so
/// <c>Program.cs</c> is a single call and each verb is a self-contained <see cref="Command"/>.
/// </summary>
public static class ForgeCli
{
    /// <summary>Parse <paramref name="args"/>, dispatch to a verb, and return the process exit code.</summary>
    public static int Run(string[] args)
    {
        var pretty = ResolvePretty();
        Theme.Enabled = pretty;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            HelpPrinter.Print(pretty);
            // No verb at all is a usage error (nothing was done); an explicit help request is success.
            return args.Length == 0 ? ExitCodes.Usage : ExitCodes.Ok;
        }

        return args[0] switch
        {
            "keygen" => new KeygenCommand(pretty).Execute(args),
            "mint" => new MintCommand(pretty).Execute(args),
            _ => Unknown(args[0], pretty),
        };
    }

    private static int Unknown(string verb, bool pretty)
    {
        if (pretty)
            Render.Error($"unknown command '{verb}'.");
        else
            Console.Error.WriteLine($"error: unknown command '{verb}'.");
        HelpPrinter.Print(pretty);
        return ExitCodes.Usage;
    }

    /// <summary>
    /// Decide whether to emit 24-bit colour. <c>FORCE_COLOR</c> wins outright (for CI that records
    /// styled logs); otherwise colour is on only for an interactive terminal with <c>NO_COLOR</c>
    /// unset — so a pipe or redirect gets clean, scriptable text.
    /// </summary>
    private static bool ResolvePretty()
    {
        if (Environment.GetEnvironmentVariable("FORCE_COLOR") is { Length: > 0 })
            return true;
        return !Console.IsOutputRedirected
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
    }
}
