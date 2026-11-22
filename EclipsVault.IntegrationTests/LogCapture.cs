using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace EclipsVault.IntegrationTests;

/// <summary>A log sink that keeps what the vault logs, so a test can read it as an operator would.</summary>
public sealed class LogCapture : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    /// <summary>Each line as written, with its exception, if any, appended.</summary>
    public IReadOnlyList<(LogEventLevel Level, string Text)> Lines
        => _events.Select(e => (e.Level, $"{e.RenderMessage()} {e.Exception}")).ToList();
}
