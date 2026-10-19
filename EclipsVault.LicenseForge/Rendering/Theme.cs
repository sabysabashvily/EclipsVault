namespace EclipsVault.LicenseForge.Rendering;

/// <summary>
/// The tool's visual theme — 24-bit ANSI colour with semantic names, so a restyle is a one-file
/// change. The palette follows EclipsVault's own identity (an umbra-dark field with an amber corona
/// accent), rather than borrowing another product's blue. Colour escapes collapse to empty strings
/// when <see cref="Enabled"/> is false, so the same rendering code produces clean plain text when
/// output is piped or redirected.
/// </summary>
public static class Theme
{
    public readonly record struct Rgb(byte R, byte G, byte B);

    /// <summary>Primary accent — amber corona.</summary>
    public static readonly Rgb Accent = new(0xF5, 0x9E, 0x0B);

    /// <summary>Recessed border amber — borders that should not compete with content.</summary>
    public static readonly Rgb AccentDeep = new(0x9A, 0x5B, 0x06);

    public static readonly Rgb Positive = new(0x4A, 0xDE, 0x80);
    public static readonly Rgb Negative = new(0xF8, 0x71, 0x71);
    public static readonly Rgb Warning = new(0xFB, 0xBF, 0x24);
    public static readonly Rgb Text = new(0xEC, 0xEF, 0xF1);
    public static readonly Rgb Muted = new(0x8A, 0x8F, 0x98);
    public static readonly Rgb Subtle = new(0x5A, 0x5F, 0x68);

    /// <summary>Corona gradient (gold → amber → ember) for the wordmark and rules.</summary>
    public static readonly Rgb[] Corona =
    [
        new(0xFF, 0xE0, 0x8A),
        new(0xF5, 0x9E, 0x0B),
        new(0xE0, 0x5A, 0x2B),
    ];

    /// <summary>When false, every escape below is empty — plain, unstyled output for pipes/CI.</summary>
    public static bool Enabled { get; set; } = true;

    public static string Fg(Rgb c) => Enabled ? $"\x1b[38;2;{c.R};{c.G};{c.B}m" : "";
    public static string Bg(Rgb c) => Enabled ? $"\x1b[48;2;{c.R};{c.G};{c.B}m" : "";
    public static string Reset => Enabled ? "\x1b[0m" : "";
    public static string Bold => Enabled ? "\x1b[1m" : "";
    public static string Dim => Enabled ? "\x1b[2m" : "";

    /// <summary>Sample the corona gradient at <paramref name="t"/> ∈ [0, 1].</summary>
    public static Rgb Sample(double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        var scaled = t * (Corona.Length - 1);
        var lo = (int)Math.Floor(scaled);
        var hi = Math.Min(lo + 1, Corona.Length - 1);
        var f = scaled - lo;
        var a = Corona[lo];
        var b = Corona[hi];
        return new Rgb(
            (byte)Math.Round(a.R + (b.R - a.R) * f),
            (byte)Math.Round(a.G + (b.G - a.G) * f),
            (byte)Math.Round(a.B + (b.B - a.B) * f));
    }
}
