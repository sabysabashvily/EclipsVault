using EclipsVault.LicenseForge.Commands;

namespace EclipsVault.LicenseForge.Rendering;

/// <summary>
/// Renders the usage screen for both audiences: a terse four-line synopsis when output is piped (so
/// <c>--help</c> in a script stays greppable) and a themed banner-plus-sections screen on a terminal.
/// Modelled on the TradeCore CLI's HelpPrinter — one place owns the command reference.
/// </summary>
public static class HelpPrinter
{
    public static void Print(bool pretty)
    {
        if (pretty)
            PrintThemed();
        else
            PrintPlain();
    }

    private static void PrintPlain()
    {
        Console.WriteLine("EclipsVault license tool");
        Console.WriteLine("  keygen [--out <path>]");
        Console.WriteLine("  mint --tier <Community|Max> --to <name> [--key-file <path>] [--contact <email>]");
        Console.WriteLine("       [--nodes <n>] [--years <n>] [--expires <n>] [--trial-days <n>] [--features a,b,c] [--id <id>]");
        Console.WriteLine("  mint reads the private key from --key-file only. keygen needs --out <path>.");
    }

    private static void PrintThemed()
    {
        Banner.Print();

        Render.SectionHeader("Commands");
        Console.WriteLine($"  {Theme.Fg(Theme.Accent)}{Theme.Bold}{"keygen",-10}{Theme.Reset} {Theme.Fg(Theme.Muted)}generate a P-256 signing keypair (run once){Theme.Reset}");
        Console.WriteLine($"  {Theme.Fg(Theme.Accent)}{Theme.Bold}{"mint",-10}{Theme.Reset} {Theme.Fg(Theme.Muted)}sign a license token from claims{Theme.Reset}");

        Render.SectionHeader("keygen options");
        Option("--out", "write the private key to this file (owner-only) and never display it");
        Console.WriteLine();

        Render.SectionHeader("mint options");
        Option("--key-file", "read the private key from this file — preferred over the env var");
        Option("--tier", "Community | Max                (required, unless --trial-days)");
        Option("--to", "customer / licensee name       (required)");
        Option("--contact", "customer email");
        Option("--nodes", "node allowance (0 = unlimited)");
        Option("--years", "update window in years (default 1; the licence stays perpetual)");
        Option("--expires", "hard expiry in years for a time-limited licence, e.g. an eval (default: perpetual)");
        Option("--trial-days", "mint a Max trial with an n-day hard expiry, e.g. 30 (implies --tier Max)");
        Option("--features", "comma list to override the tier default");
        Option("--id", "license id (default: random)");
        Console.WriteLine();
        Render.Info("mint reads the private key from --key-file only (base64 PKCS#8). keygen needs --out <path>.");
        Render.Info("A key file keeps the key out of your shell history and out of the process list.");
        Console.WriteLine();
    }

    private static void Option(string name, string description)
        => Console.WriteLine($"  {Theme.Fg(Theme.Text)}{name,-12}{Theme.Reset} {Theme.Fg(Theme.Muted)}{description}{Theme.Reset}");
}
