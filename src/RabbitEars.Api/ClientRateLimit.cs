using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;

namespace RabbitEars.Api;

/// <summary>
/// One allowance of requests per client address, shared by plain HTTP requests
/// and calls over the hub, so no single phone or visitor can flood the box.
/// </summary>
/// <remarks>
/// It is a token bucket: a client may spend <see cref="RateLimitOptions.Burst"/>
/// at once (a page load pulls the app, the picture settings and video ranges
/// together) and gets <see cref="RateLimitOptions.RequestsPerSecond"/> back
/// every second. The box itself is never limited; its own screen talks to it
/// over loopback.
/// </remarks>
public static class ClientRateLimit
{
    /// <summary>Adds the limiter, if switched on. Pair with <see cref="UseClientRateLimit"/>.</summary>
    public static void AddClientRateLimit(this IServiceCollection services, RateLimitOptions options)
    {
        if (options.TrustForwardedFor)
        {
            services.Configure<ForwardedHeadersOptions>(forwarded =>
            {
                forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
                // The reverse proxy reaches the container from a Docker bridge
                // address, not loopback, so the defaults would ignore it. The
                // container only listens behind that proxy, which sets the header.
                forwarded.KnownIPNetworks.Clear();
                forwarded.KnownProxies.Clear();
            });
        }

        if (!options.Enabled) return;

        var limiter = Create(options);
        services.AddSingleton(limiter);
        services.AddRateLimiter(rate =>
        {
            rate.GlobalLimiter = limiter;
            rate.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rate.OnRejected = (rejected, _) =>
            {
                rejected.HttpContext.Response.Headers.RetryAfter =
                    RetryAfterSeconds(rejected.Lease, options).ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
        });
        services.Configure<HubOptions>(hub => hub.AddFilter<RateLimitHubFilter>());
    }

    /// <summary>Goes first in the pipeline, so static files and the hub are counted too.</summary>
    public static void UseClientRateLimit(this WebApplication app, RateLimitOptions options)
    {
        if (options.TrustForwardedFor) app.UseForwardedHeaders();
        if (options.Enabled) app.UseRateLimiter();
    }

    public static PartitionedRateLimiter<HttpContext> Create(RateLimitOptions options)
    {
        // Whole tokens per period; a fractional rate stretches the period instead.
        var tokens = Math.Max(1, (int)Math.Round(options.RequestsPerSecond));
        var bucket = new TokenBucketRateLimiterOptions
        {
            TokenLimit = Math.Max(1, options.Burst),
            TokensPerPeriod = tokens,
            ReplenishmentPeriod = TimeSpan.FromSeconds(tokens / options.RequestsPerSecond),
            QueueLimit = 0,
            AutoReplenishment = true,
        };

        return PartitionedRateLimiter.Create<HttpContext, string>(context =>
            context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address)
                ? RateLimitPartition.GetNoLimiter("loopback")
                : RateLimitPartition.GetTokenBucketLimiter(ClientKey(context.Connection.RemoteIpAddress), _ => bucket));
    }

    /// <summary>
    /// The address a client is counted under. An IPv6 home connection is
    /// usually handed a whole /64, so the client is that network, not each
    /// address in it.
    /// </summary>
    public static string ClientKey(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    private static int RetryAfterSeconds(RateLimitLease lease, RateLimitOptions options)
    {
        var wait = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? retryAfter
            : TimeSpan.FromSeconds(1 / options.RequestsPerSecond);
        return Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
    }
}

/// <summary>
/// Counts each hub call against the caller's allowance. Over a WebSocket a
/// remote's button presses are not HTTP requests, so the middleware never sees them.
/// </summary>
public sealed class RateLimitHubFilter(PartitionedRateLimiter<HttpContext> limiter) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (invocation.Context.GetHttpContext() is { } http)
        {
            using var lease = limiter.AttemptAcquire(http);
            if (!lease.IsAcquired) throw new HubException("Too many requests. Slow down.");
        }
        return await next(invocation);
    }
}
