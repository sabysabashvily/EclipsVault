using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class SmtpCapture : IAsyncDisposable
{
    internal const string Image = "axllent/mailpit:v1.27.8@sha256:6abc8e633df15eaf785cfcf38bae48e66f64beecdc03121e249d0f9ec15f0707";
    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithName($"eclipsvault-mailbox-{Guid.NewGuid():N}")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025)))
        .Build();

    public string Host => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(1025);
    public Task StartAsync() => _container.StartAsync();
    public Task PauseAsync() => _container.PauseAsync();
    public Task ResumeAsync() => _container.UnpauseAsync();
    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task<bool> HasMessageForAsync(string address)
    {
        var messages = await MessagesAsync();
        return messages.Any(message => IsAddressedTo(message, address));
    }

    public async Task<bool> HasDecisionForAsync(string address)
    {
        var messages = await MessagesAsync();
        return messages.Any(message =>
            message.GetProperty("Subject").GetString() is "Your access request was approved" or "Your access request was declined"
            && IsAddressedTo(message, address));
    }

    public async Task<string> CodeForAsync(string address)
    {
        using var client = NewClient();
        var messages = await MessagesAsync();
        var message = messages.First(message => IsAddressedTo(message, address)
            && message.GetProperty("Subject").GetString() == "Confirm your EclipsVault email");
        var body = await client.GetFromJsonAsync<JsonElement>($"/api/v1/message/{message.GetProperty("ID").GetString()}");
        return Regex.Match(body.GetProperty("Text").GetString()!, "change: ([A-F0-9]{32})").Groups[1].Value;
    }

    private HttpClient NewClient() => new() { BaseAddress = new Uri($"http://{Host}:{_container.GetMappedPublicPort(8025)}") };

    private async Task<JsonElement[]> MessagesAsync()
    {
        using var client = NewClient();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/v1/messages");
        return result.GetProperty("messages").EnumerateArray().ToArray();
    }

    private static bool IsAddressedTo(JsonElement message, string address)
        => message.GetProperty("To").EnumerateArray().Any(to => to.GetProperty("Address").GetString() == address);
}
