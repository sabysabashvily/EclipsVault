using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Security.WebAuthn;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace EclipsVault.Tests.Profile;

public sealed class PasskeyCeremonyStoreTests
{
    [Fact]
    public async Task Replacing_a_ceremony_preserves_the_other_purpose()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryPasskeyCeremonyStore(cache, TimeProvider.System);
        await store.ReplaceAsync("browser", PasskeyCeremonyPurpose.SignIn, "old", default);
        await store.ReplaceAsync("browser", PasskeyCeremonyPurpose.Registration, "registration", default);

        await store.ReplaceAsync("browser", PasskeyCeremonyPurpose.SignIn, "new", default);

        Assert.Equal("new", await store.TakeAsync("browser", PasskeyCeremonyPurpose.SignIn, default));
        Assert.Equal("registration", await store.TakeAsync("browser", PasskeyCeremonyPurpose.Registration, default));
        Assert.Null(await store.TakeAsync("another-browser", PasskeyCeremonyPurpose.SignIn, default));
    }

    [Fact]
    public async Task Two_completions_can_take_a_ceremony_only_once()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryPasskeyCeremonyStore(cache, TimeProvider.System);
        await store.ReplaceAsync("browser", PasskeyCeremonyPurpose.SignIn, "state", default);

        var results = await Task.WhenAll(Task.Run(() => store.TakeAsync("browser", PasskeyCeremonyPurpose.SignIn, default)),
            Task.Run(() => store.TakeAsync("browser", PasskeyCeremonyPurpose.SignIn, default)));

        Assert.Single(results, result => result == "state");
        Assert.Single(results, result => result is null);
    }

    [Fact]
    public async Task A_ceremony_expires_after_120_seconds_without_session_activity_extending_it()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var clock = new CeremonyClock();
        var store = new MemoryPasskeyCeremonyStore(cache, clock);
        await store.ReplaceAsync("browser", PasskeyCeremonyPurpose.SignIn, "state", default);
        clock.Advance(TimeSpan.FromSeconds(120));

        Assert.Null(await store.TakeAsync("browser", PasskeyCeremonyPurpose.SignIn, default));
    }

    private sealed class CeremonyClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
