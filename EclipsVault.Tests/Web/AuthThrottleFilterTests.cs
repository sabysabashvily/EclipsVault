using System.Net;
using System.Reflection;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Infrastructure.Security;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.Tests.Web;

public sealed class AuthThrottleFilterTests
{
    private sealed class FrozenClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingThrottle(bool allow) : IAuthThrottle
    {
        public List<string> Partitions { get; } = [];
        public Task<bool> TryAcquireAsync(string partitionKey, CancellationToken ct)
        {
            Partitions.Add(partitionKey);
            return Task.FromResult(allow);
        }
    }

    private sealed class OtherController : Controller
    {
        public void Logout() { }
    }

    private static ControllerActionDescriptor AccountPost(string name)
        => new()
        {
            ControllerTypeInfo = typeof(AccountController).GetTypeInfo(),
            ControllerName = "Account",
            ActionName = name,
            MethodInfo = typeof(AccountController).GetMethods()
                .Single(method => method.Name == name && method.IsDefined(typeof(HttpPostAttribute)))
        };

    private static async Task<(ActionExecutingContext Context, bool Called)> RunAsync(
        IAuthThrottle throttle, ActionDescriptor action, string method = "POST", string? address = "198.51.100.90")
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Connection.RemoteIpAddress = address is null ? null : IPAddress.Parse(address);
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), action), [], new Dictionary<string, object?>(), new object());
        var called = false;
        await new AuthThrottleFilter(throttle, NullLogger<AuthThrottleFilter>.Instance)
            .OnActionExecutionAsync(context, () =>
            {
                called = true;
                return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
            });
        return (context, called);
    }

    [Fact]
    public async Task Logout_succeeds_when_the_real_credential_budget_is_exhausted()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var throttle = new InMemoryAuthThrottle(cache, Options.Create(new AuthThrottleOptions { PermitLimit = 1 }), new FrozenClock());
        Assert.True(await throttle.TryAcquireAsync("198.51.100.90", CancellationToken.None));
        Assert.False(await throttle.TryAcquireAsync("198.51.100.90", CancellationToken.None));

        var (context, called) = await RunAsync(throttle, AccountPost(nameof(AccountController.Logout)));

        Assert.Null(context.Result);
        Assert.True(called);
        var (credential, credentialCalled) = await RunAsync(throttle, AccountPost(nameof(AccountController.Login)));
        Assert.Equal(429, Assert.IsType<StatusCodeResult>(credential.Result).StatusCode);
        Assert.False(credentialCalled);
    }

    [Fact]
    public async Task Logout_does_not_spend_the_remaining_credential_attempt()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var throttle = new InMemoryAuthThrottle(cache, Options.Create(new AuthThrottleOptions { PermitLimit = 1 }), new FrozenClock());

        var (_, called) = await RunAsync(throttle, AccountPost(nameof(AccountController.Logout)));

        Assert.True(called);
        Assert.True(await throttle.TryAcquireAsync("198.51.100.90", CancellationToken.None));
        Assert.False(await throttle.TryAcquireAsync("198.51.100.90", CancellationToken.None));
    }

    [Fact]
    public async Task Every_other_unsafe_account_action_remains_throttled()
    {
        Assert.Contains(typeof(AccountController).GetCustomAttributes<ServiceFilterAttribute>(),
            filter => filter.ServiceType == typeof(AuthThrottleFilter));
        var posts = typeof(AccountController).GetMethods()
            .Where(method => method.IsDefined(typeof(HttpPostAttribute)))
            .Select(method => method.Name).Order(StringComparer.Ordinal).ToArray();
        string[] metered = ["EnrollTotp", "ExternalLogin", "Login", "PasskeyLoginBegin", "PasskeyLoginComplete", "Recover", "RecoveryCode", "Totp"];
        Assert.Equal(metered.Append("Logout").Order(StringComparer.Ordinal), posts);

        foreach (var name in metered)
        {
            var throttle = new RecordingThrottle(allow: false);
            var (context, called) = await RunAsync(throttle, AccountPost(name));
            Assert.Equal(429, Assert.IsType<StatusCodeResult>(context.Result).StatusCode);
            Assert.False(called, name);
            Assert.Single(throttle.Partitions);
        }
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task An_unknown_action_even_named_Logout_is_throttled_by_default(string method)
    {
        var action = new ActionDescriptor { DisplayName = "Account.Logout" };
        var (context, called) = await RunAsync(new RecordingThrottle(allow: false), action, method);
        Assert.Equal(429, Assert.IsType<StatusCodeResult>(context.Result).StatusCode);
        Assert.False(called);
    }

    [Fact]
    public async Task Another_controllers_Logout_action_is_still_throttled()
    {
        var action = new ControllerActionDescriptor
        {
            ControllerTypeInfo = typeof(OtherController).GetTypeInfo(),
            ControllerName = "Other",
            ActionName = "Logout",
            MethodInfo = typeof(OtherController).GetMethod(nameof(OtherController.Logout))!
        };
        var (context, called) = await RunAsync(new RecordingThrottle(allow: false), action);
        Assert.Equal(429, Assert.IsType<StatusCodeResult>(context.Result).StatusCode);
        Assert.False(called);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Safe_requests_do_not_spend_a_credential_attempt(string method)
    {
        var throttle = new RecordingThrottle(allow: false);
        var (_, called) = await RunAsync(throttle, new ActionDescriptor(), method);
        Assert.True(called);
        Assert.Empty(throttle.Partitions);
    }

    [Theory]
    [InlineData("198.51.100.90", "198.51.100.90")]
    [InlineData("::ffff:198.51.100.90", "198.51.100.90")]
    [InlineData(null, "unknown")]
    public async Task Credential_attempts_keep_the_normalized_shared_partition(string? address, string expected)
    {
        var throttle = new RecordingThrottle(allow: true);
        var (_, called) = await RunAsync(throttle, AccountPost(nameof(AccountController.Login)), address: address);
        Assert.True(called);
        Assert.Equal(expected, Assert.Single(throttle.Partitions));
    }
}
