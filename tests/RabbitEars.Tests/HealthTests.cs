using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using RabbitEars.Api;

namespace RabbitEars.Tests;

public sealed class HealthTests : IDisposable
{
    private readonly string _media = Directory.CreateTempSubdirectory("rabbitears-media-").FullName;
    private readonly string _state = Directory.CreateTempSubdirectory("rabbitears-state-").FullName;

    private WebApplicationFactory<Program> Box() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("Box:MediaRoot", _media)
            .UseSetting("Box:StateDir", _state)
            .UseSetting("Box:RescanMinutes", "0"));

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Health(WebApplicationFactory<Program> box)
    {
        using var client = box.CreateClient();
        var response = await client.GetAsync("/health");
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task Health_is_healthy_with_channels_and_never_names_the_media_folder()
    {
        var episode = Path.Combine(_media, "Arthur", "S01E01.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(episode)!);
        File.WriteAllText(episode, "not really a video");
        await using var box = Box();

        var (status, body) = await Health(box);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("library").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("player").GetString());
        Assert.Equal(1, body.GetProperty("details").GetProperty("library").GetProperty("data").GetProperty("channels").GetInt32());
        Assert.DoesNotContain(_media, body.GetRawText());
        Assert.DoesNotContain("Arthur", body.GetRawText());
    }

    [Fact]
    public async Task Health_is_degraded_but_still_200_when_the_library_is_empty()
    {
        await using var box = Box();

        var (status, body) = await Health(box);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Degraded", body.GetProperty("status").GetString());
        Assert.Contains("colour bars", body.GetProperty("details").GetProperty("library").GetProperty("description").GetString());
    }

    [Fact]
    public void Telemetry_registers_nothing_without_an_OTLP_endpoint()
    {
        var builder = WebApplication.CreateBuilder();

        Assert.False(builder.AddTelemetry(0.25, endpoint: ""));
        using var app = builder.Build();
        Assert.Null(app.Services.GetService<TracerProvider>());
    }

    [Fact]
    public void Telemetry_builds_its_providers_when_an_OTLP_endpoint_is_set()
    {
        var builder = WebApplication.CreateBuilder();

        Assert.True(builder.AddTelemetry(0.25, endpoint: "http://localhost:4318"));
        using var app = builder.Build();
        Assert.NotNull(app.Services.GetService<TracerProvider>());
    }

    public void Dispose()
    {
        Directory.Delete(_media, recursive: true);
        Directory.Delete(_state, recursive: true);
    }
}
