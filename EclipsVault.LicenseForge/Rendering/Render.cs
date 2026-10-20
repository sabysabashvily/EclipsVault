using System.IO;
using System.Text;

namespace EclipsVault.LicenseForge.Rendering;

/// <summary>
/// Pure console rendering primitives — chips, section headers, gradient rules, bordered cards, and
/// copy-paste key blocks. Modelled on the TradeCore CLI's rendering layer, retuned to EclipsVault's
/// palette. No business logic; every method writes to <see cref="Console"/> only.
/// </summary>
public static class Render
{
    public static void Success(string m) => Chip(Theme.Positive, "✓", m);
    public static void Warn(string m) => Chip(Theme.Warning, "!", m);
    public static void Error(string m) => Chip(Theme.Negative, "✗", m);
    public static void Info(string m) => Console.WriteLine($"  {Theme.Fg(Theme.Muted)}{m}{Theme.Reset}");

    private static void Chip(Theme.Rgb c, string glyph, string m)
        => Console.WriteLine(
            $"  {Theme.Bg(c)}{Theme.Fg(Theme.Text)}{Theme.Bold} {glyph} {Theme.Reset}" +
            $"{Theme.Fg(c)}▌{Theme.Reset} {Theme.Fg(Theme.Text)}{m}{Theme.Reset}");

    public static void SectionHeader(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"  {Theme.Fg(Theme.Accent)}{Theme.Bold}{title.ToUpperInvariant()}{Theme.Reset}");
    }

    /// <summary>A thin rule painted along the corona gradient.</summary>
    public static void GradientRule(int width)
    {
        var sb = new StringBuilder("  ");
        for (var i = 0; i < width; i++)
        {
            var t = width == 1 ? 0d : (double)i / (width - 1);
            sb.Append(Theme.Fg(Theme.Sample(t))).Append('─');
        }
        sb.Append(Theme.Reset);
        Console.WriteLine(sb.ToString());
    }

    /// <summary>
    /// A bordered card of <c>label : value</c> rows, values in their own colour. For short values
    /// (claims, metadata); long values belong in <see cref="KeyBlock"/>, which cannot break a border.
    /// </summary>
    public static void Card(string title, IReadOnlyList<(string Label, string Value, Theme.Rgb Color)> rows)
    {
        const int labelCol = 12;
        var inner = labelCol + 3;
        foreach (var r in rows)
            inner = Math.Max(inner, labelCol + 3 + r.Value.Length);
        var contentWidth = inner + 2;
        var border = Theme.Fg(Theme.AccentDeep);

        Console.WriteLine();
        if (!string.IsNullOrEmpty(title))
            Console.WriteLine($"  {Theme.Fg(Theme.Accent)}{Theme.Bold}{title.ToUpperInvariant()}{Theme.Reset}");

        Console.WriteLine($"  {border}╭{new string('─', contentWidth)}╮{Theme.Reset}");
        foreach (var r in rows)
        {
            var label = r.Label.PadRight(labelCol);
            var visible = 1 + labelCol + 3 + r.Value.Length; // " " + label + " : " + value
            var pad = Math.Max(0, contentWidth - visible - 1);
            Console.WriteLine(
                $"  {border}│{Theme.Reset} {Theme.Fg(Theme.Muted)}{label}{Theme.Reset} " +
                $"{Theme.Fg(Theme.AccentDeep)}:{Theme.Reset} {Theme.Fg(r.Color)}{r.Value}{Theme.Reset}" +
                $"{new string(' ', pad)} {border}│{Theme.Reset}");
        }
        Console.WriteLine($"  {border}╰{new string('─', contentWidth)}╯{Theme.Reset}");
    }

    /// <summary>
    /// A labelled block for a long, copy-paste value (a key or a token). The value prints on its own
    /// line, unbroken, so a terminal selection copies it whole; it is framed by thin rules rather than
    /// a box, so a soft-wrap of the value can never collide with a right border.
    /// </summary>
    public static void KeyBlock(string label, Theme.Rgb labelColor, string value, string? caution)
    {
        var rule = new string('─', RuleWidth());
        Console.WriteLine();
        var tail = caution is null ? "" : $"  {Theme.Fg(Theme.Muted)}{caution}{Theme.Reset}";
        Console.WriteLine($"  {Theme.Bg(labelColor)}{Theme.Fg(Theme.Text)}{Theme.Bold} {label} {Theme.Reset}{tail}");
        Console.WriteLine($"  {Theme.Fg(Theme.Subtle)}{rule}{Theme.Reset}");
        Console.WriteLine($"  {Theme.Fg(Theme.Text)}{value}{Theme.Reset}");
        Console.WriteLine($"  {Theme.Fg(Theme.Subtle)}{rule}{Theme.Reset}");
    }

    /// <summary>Terminal width for rules, clamped and safe when the width is unavailable.</summary>
    public static int RuleWidth()
    {
        if (Console.IsOutputRedirected) return 60;
        try
        {
            var w = Console.WindowWidth;
            return w > 8 ? Math.Min(w - 4, 76) : 60;
        }
        catch (IOException)
        {
            return 60;
        }
    }
}
