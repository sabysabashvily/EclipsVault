using System.Reflection;
using EclipsVault.Web.Controllers;
using Microsoft.AspNetCore.Razor.Hosting;
using Xunit;

namespace EclipsVault.Tests.Web;

/// <summary>
/// ADR-0001 is met only when no Razor view remains (ADR-0034): the vault compiles none, and nothing
/// still posts the Legacy sign-out, which went with them.
/// </summary>
public sealed class RazorRemovalTests
{
    [Fact]
    public void No_Razor_view_is_compiled_into_the_vault()
    {
        Assert.Empty(typeof(VaultController).Assembly.GetCustomAttributes<RazorCompiledItemAttribute>());
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(RepoRoot(), "EclipsVault.Web"), "*.cshtml", SearchOption.AllDirectories));
    }

    [Fact]
    public void No_application_code_posts_the_Legacy_sign_out()
    {
        string[] sources = ["EclipsVault.Web", Path.Combine("EclipsVault.AngularUi", "src")];
        var callers = sources
            .SelectMany(source => Directory.EnumerateFiles(Path.Combine(RepoRoot(), source), "*", SearchOption.AllDirectories))
            .Where(file => Path.GetExtension(file) is ".cs" or ".ts" or ".html" or ".js")
            // Build output, the Angular bundles included, is made from these sources.
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "wwwroot"))
            .Where(file => File.ReadAllText(file).Contains("/Account/Logout", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(callers.Count == 0,
            "These files still name /Account/Logout; sign out through POST /api/ui/auth/sign-out:\n  " + string.Join("\n  ", callers));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EclipsVault.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate EclipsVault.slnx above the test output directory.");
        return dir!.FullName;
    }
}
