using EclipsVault.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EclipsVault.Tests.Web;

public sealed class ServerPageTests
{
    [Fact]
    public async Task The_not_found_page_says_there_is_no_page_and_offers_the_way_back()
    {
        var (status, html) = await RenderAsync(ServerPage.NotFoundAsync);

        Assert.Equal(404, status);
        Assert.Contains("<title>Page not found — EclipsVault</title>", html);
        Assert.Contains("<h1>404 — Page not found</h1>", html);
        Assert.Contains("""<p class="muted">There is no page at this address.</p>""", html);
        Assert.Contains("""<a class="button primary wide" href="/">Back to safety</a>""", html);
        Assert.Contains("""<link rel="stylesheet" href="/css/server-page.css" />""", html);
        Assert.DoesNotContain("<script", html);
    }

    [Theory]
    [InlineData(null, "dark")]
    [InlineData("EclipsVault.Theme=light", "light")]
    [InlineData("EclipsVault.Theme=dark", "dark")]
    [InlineData("EclipsVault.Theme=\"><script>", "dark")]
    public async Task The_theme_is_light_only_when_the_cookie_says_light(string? cookie, string theme)
    {
        var (_, html) = await RenderAsync(ServerPage.NotFoundAsync, cookie);

        Assert.Contains($"""<html lang="en" data-theme="{theme}">""", html);
    }

    private static async Task<(int Status, string Html)> RenderAsync(Func<HttpContext, Task> write, string? cookie = null)
    {
        var context = new DefaultHttpContext();
        if (cookie is not null)
        {
            context.Request.Headers.Cookie = cookie;
        }

        await using var body = new MemoryStream();
        context.Response.Body = body;
        await write(context);
        Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
        return (context.Response.StatusCode, System.Text.Encoding.UTF8.GetString(body.ToArray()));
    }
}
