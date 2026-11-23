using EclipsVault.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.Tests.Web;

public sealed class FailedDownloadResponseTests
{
    [Fact]
    public async Task A_download_that_fails_before_its_first_byte_answers_a_problem_without_its_file_name()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/ui/audit/export";
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionMiddleware(failing =>
        {
            failing.Response.Headers.ContentDisposition = "attachment; filename=eclipsvault-audit-bundle-20261113-120000.json";
            throw new InvalidOperationException("The database connection dropped while reading the first row.");
        }, NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        Assert.False(context.Response.Headers.ContainsKey("Content-Disposition"));
    }
}
