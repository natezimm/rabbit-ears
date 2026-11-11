using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RabbitEars.Api;

/// <summary>
/// Whether the TV can show anything: the library found channels (without them it shows colour bars),
/// and on the box, whether mpv's control socket is open. The demo also reports how full it is.
/// </summary>
public sealed class LibraryHealth(TvService tv, BoxOptions options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var report = tv.Report();
        var data = new Dictionary<string, object>
        {
            ["channels"] = report.Channels.Count,
            ["episodes"] = report.Episodes,
            ["needsConversion"] = report.NeedsConversion.Count,
        };
        if (options.Demo.Enabled)
        {
            data["televisions"] = tv.RoomCount;
            data["connections"] = tv.ConnectionCount;
        }
        return Task.FromResult(report.Channels.Count > 0
            ? HealthCheckResult.Healthy($"{report.Channels.Count} channels, {report.Episodes} episodes.", data)
            : HealthCheckResult.Degraded("No channels: the TV is showing colour bars. Check the media folder.", data: data));
    }
}

/// <summary>On the box, mpv is the screen; without its socket nothing is drawn.</summary>
public sealed class PlayerHealth(BoxOptions options, MpvPlayer player) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(!options.UsesMpv
            ? HealthCheckResult.Healthy("The browser is the screen.")
            : player.Connected
                ? HealthCheckResult.Healthy("Connected to mpv.")
                // Degraded, not Unhealthy: the kiosk restarting mpv is normal, and the API itself is fine.
                : HealthCheckResult.Degraded("Waiting for mpv's control socket."));
}

public static class HealthEndpoints
{
    public static IServiceCollection AddTvHealth(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<LibraryHealth>("library")
            .AddCheck<PlayerHealth>("player");
        return services;
    }

    /// <summary>
    /// <c>/health</c>: each check's status and what it measured, for uptime checks and a person reading it.
    /// 200 for Healthy and Degraded, 503 only for Unhealthy. It never names paths, rooms or files.
    /// <c>/api/health</c> stays the cheap "is it up" answer it always was.
    /// </summary>
    public static IEndpointRouteBuilder MapTvHealth(this IEndpointRouteBuilder app)
    {
        var startedAt = DateTimeOffset.UtcNow;
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
            ResponseWriter = (context, report) =>
            {
                context.Response.ContentType = "application/json";
                context.Response.Headers.CacheControl = "no-store";
                return context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    status = report.Status.ToString(),
                    startedAt,
                    checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
                    details = report.Entries.ToDictionary(e => e.Key, e => new
                    {
                        description = e.Value.Description,
                        durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                        data = e.Value.Data,
                    }),
                }, JsonSerializerOptions.Web));
            },
        });
        return app;
    }
}
