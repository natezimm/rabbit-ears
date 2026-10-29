using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using RabbitEars.Api;
using RabbitEars.Core;

namespace RabbitEars.Tests;

public sealed class DemoProtectionTests
{
    [Fact]
    public async Task Hosted_media_supports_ranges_and_head_and_refuses_multiple_ranges()
    {
        var root = Directory.CreateTempSubdirectory("ears-protection-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "media", "Show"));
            await File.WriteAllBytesAsync(Path.Combine(root, "media", "Show", "Tape.mp4"), new byte[1000]);
            await using var app = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>()
                .WithWebHostBuilder(host =>
                {
                    host.UseSetting("Box:MediaRoot", Path.Combine(root, "media"));
                    host.UseSetting("Box:StateDir", Path.Combine(root, "state"));
                    host.UseSetting("Box:Demo:Enabled", "true");
                    host.UseSetting("Box:Player:Kind", "browser");
                    host.UseSetting("Box:Ffprobe", "");
                });
            using var client = app.CreateClient();
            var tv = app.Services.GetRequiredService<TvService>();
            tv.Join("test-room");
            var url = "/media/" + tv.Snapshot("test-room")!.Playing!.EpisodeId;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(100, 199);
            using var response = await client.SendAsync(request);
            Assert.Equal(System.Net.HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal(100, (await response.Content.ReadAsByteArrayAsync()).Length);
            using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
            Assert.Equal(1000, head.Content.Headers.ContentLength);
            Assert.Empty(await head.Content.ReadAsByteArrayAsync());
            using var multiple = new HttpRequestMessage(HttpMethod.Get, url);
            multiple.Headers.TryAddWithoutValidation("Range", "bytes=0-10,20-30");
            Assert.Equal(System.Net.HttpStatusCode.RequestedRangeNotSatisfiable, (await client.SendAsync(multiple)).StatusCode);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Browser_and_server_selection_limits_agree()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RabbitEars.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var policy = File.ReadAllText(Path.Combine(dir.FullName, "web/src/app/core/tape-policy.ts"));
        Assert.Contains($"MAX_SHELVES = {Tapes.MaxShelves};", policy);
        Assert.Contains($"MAX_TAPES = {Tapes.MaxTapes};", policy);
        Assert.NotNull(Tapes.Channels([], [new(null, [.. Enumerable.Range(0, 5).Select(i => new Tape($"{i:x8}", "Tape"))])]));
        Assert.Null(Tapes.Channels([], [.. Enumerable.Range(0, 4).Select(i => new TapeShelf($"Shelf{i}", [new Tape($"{i:x8}", "Tape")]))]));
    }

    [Fact]
    public void Address_budget_expires_and_is_bounded()
    {
        var budget = new DemoWindowBudget();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++) Assert.True(budget.TryTake("one", now, 5));
        Assert.False(budget.TryTake("one", now, 5));
        for (var i = 0; i < 1023; i++) Assert.True(budget.TryTake($"other{i}", now, 5));
        Assert.False(budget.TryTake("overflow", now, 5));
        Assert.True(budget.TryTake("overflow", now.AddMinutes(1), 5));
    }

    [Fact]
    public void Media_permits_bound_global_and_address_concurrency_and_release_once()
    {
        var budget = new DemoMediaBudget(new BoxOptions { Demo = new() { MaxMediaResponses = 2, MaxMediaResponsesPerAddress = 1 } });
        var first = budget.Enter("one");
        Assert.NotNull(first);
        Assert.Null(budget.Enter("one"));
        using var second = budget.Enter("two");
        Assert.NotNull(second);
        Assert.Null(budget.Enter("three"));
        first.Dispose();
        first.Dispose();
        using var third = budget.Enter("three");
        Assert.NotNull(third);
        Assert.Null(budget.Enter("four"));
    }

    [Fact]
    public async Task Parallel_media_reads_share_a_pacer_and_cancel_cleanly()
    {
        var budget = new DemoMediaBudget(new BoxOptions { Demo = new() { MediaBytesPerSecond = 1000 } });
        var watch = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => budget.Pace(100, CancellationToken.None).AsTask()));
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(180));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => budget.Pace(100, cancelled.Token).AsTask());
        await budget.Pace(1, CancellationToken.None);
    }

    [Fact]
    public async Task Budgeted_stream_preserves_seeking_and_bounds_reads()
    {
        var bytes = Enumerable.Range(0, 100_000).Select(i => (byte)(i % 251)).ToArray();
        using var stream = new BudgetedMediaStream(new MemoryStream(bytes), new DemoMediaBudget(new BoxOptions()));
        Assert.Equal(bytes.Length, stream.Length);
        stream.Seek(90_000, SeekOrigin.Begin);
        var buffer = new byte[100_000];
        var read = await stream.ReadAsync(buffer.AsMemory());
        Assert.Equal(10_000, read);
        Assert.Equal(bytes[90_000..], buffer[..read]);
        stream.Position = 0;
        Assert.Equal(32 * 1024, await stream.ReadAsync(buffer.AsMemory()));
    }
}
