using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace RabbitEars.Api;

/// <summary>
/// The TV's own metrics: how many televisions are on and who is watching. Gauges are read only when an
/// exporter asks, so with telemetry off they cost nothing. No room id, address or filename is recorded.
/// </summary>
public static class RabbitEarsTelemetry
{
    public const string Name = "RabbitEars";

    private static readonly Meter Meter = new(Name);

    /// <summary>Starts the gauges that read <paramref name="tv"/>; called once the TV exists.</summary>
    public static void Observe(TvService tv)
    {
        Meter.CreateObservableGauge("rabbitears.televisions", () => tv.RoomCount, "{television}",
            "Televisions on: one at home, one per visitor room on the demo.");
        Meter.CreateObservableGauge("rabbitears.connections", () => tv.ConnectionCount, "{connection}",
            "Screens and remotes connected to the hub.");
        Meter.CreateObservableCounter("rabbitears.changes", () => tv.ChangeCount, "{change}",
            "State changes broadcast to screens and remotes since the server started.");
    }
}

/// <summary>
/// Sends traces, metrics and logs over OTLP when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, and registers
/// nothing when it is not: a box on a home network sends nothing unless its owner asks.
/// See docs/observability.md.
/// </summary>
public static class TelemetrySetup
{
    public const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static bool AddTelemetry(this WebApplicationBuilder builder, double traceSampleRatio, string? endpoint = null)
    {
        endpoint ??= builder.Configuration[EndpointVariable];
        if (string.IsNullOrWhiteSpace(endpoint)) return false;

        builder.Logging.AddOpenTelemetry(logs =>
        {
            logs.IncludeScopes = true;
            logs.IncludeFormattedMessage = true;
        });

        var sampler = new TraceIdRatioBasedSampler(traceSampleRatio);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("rabbit-ears"))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(sampler, remoteParentSampled: sampler, remoteParentNotSampled: sampler))
                // Video is served in ranges, many per minute while a channel plays; the hub is one long socket.
                .AddAspNetCoreInstrumentation(o => o.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/media")
                    && !context.Request.Path.StartsWithSegments("/hub")
                    && !context.Request.Path.StartsWithSegments("/health")))
            .WithMetrics(metrics => metrics
                .AddMeter(RabbitEarsTelemetry.Name)
                .AddMeter(DemoMetrics.Name)
                .AddMeter("Microsoft.AspNetCore.Http.Connections")
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation())
            .WithLogging()
            .UseOtlpExporter();
        return true;
    }
}
