using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace EclipsVault.Infrastructure.Logging;

/// <summary>
/// Structured Serilog configuration: human-readable console output plus daily-rolling
/// compact JSON files ready for ingestion by a log index.
/// </summary>
public static class SerilogSetup
{
    public static Serilog.ILogger CreateBootstrapLogger()
        => new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateBootstrapLogger();

    public static IHostBuilder UseEclipsVaultSerilog(this IHostBuilder host)
        => host.UseSerilog((context, services, configuration) => configuration
            // Production registers no sinks in its services; a test host adds one to read the log.
            .ReadFrom.Services(services)
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "EclipsVault")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                new CompactJsonFormatter(),
                path: "logs/eclipsvault-.clef",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14));
}
