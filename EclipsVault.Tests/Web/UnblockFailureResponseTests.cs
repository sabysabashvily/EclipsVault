using System.Text.Json;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EclipsVault.Tests.Web;

public sealed class UnblockFailureResponseTests
{
    [Fact]
    public async Task Failed_compensation_returns_the_explicit_ui_problem_and_a_safe_critical_alert()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/ui/test-lift";
        await using var body = new MemoryStream();
        context.Response.Body = body;
        var logger = new CriticalAlert();
        var middleware = new GlobalExceptionMiddleware(_ => throw new UnblockUnrecordedException(), logger);

        await middleware.InvokeAsync(context);

        Assert.Equal(503, context.Response.StatusCode);
        body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(body);
        Assert.Equal("unblock_unrecorded", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("The block was lifted, but recording and restoring it failed. Check the range before retrying.",
            problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("An intrusion-defence block was lifted, but recording and restoring it failed", Assert.Single(logger.Messages));
        Assert.Null(logger.Exception);
    }

    private sealed class CriticalAlert : ILogger<GlobalExceptionMiddleware>
    {
        public List<string> Messages { get; } = [];
        public Exception? Exception { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Assert.Equal(LogLevel.Critical, logLevel);
            Messages.Add(formatter(state, exception));
            Exception = exception;
        }
    }
}
