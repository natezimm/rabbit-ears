using RabbitEars.Api;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("Box").Get<BoxOptions>() ?? new BoxOptions();
// Relative paths in config are relative to the app folder, wherever it is started from.
options.MediaRoot = Path.GetFullPath(options.MediaRoot, builder.Environment.ContentRootPath);
options.StateDir = Path.GetFullPath(options.StateDir, builder.Environment.ContentRootPath);
options.Player.MpvSocket = Path.GetFullPath(options.Player.MpvSocket, builder.Environment.ContentRootPath);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<LibraryScanner>();
builder.Services.AddSingleton<ChannelCatalog>();
builder.Services.AddSingleton<DemoMedia>();
builder.Services.AddSingleton<DemoMediaBudget>();
builder.Services.AddSingleton<StateFile>();
builder.Services.AddSingleton<IEpisodeProbe, FfprobeEpisodeProbe>();
builder.Services.AddSingleton<TvService>();
builder.Services.AddHostedService(services => services.GetRequiredService<TvService>());
// With mpv as the box's screen, the server drives it directly; otherwise this does nothing.
builder.Services.AddSingleton<MpvScreen>();
builder.Services.AddHostedService<MpvPlayer>();
builder.Services.AddSignalR();
builder.Services.AddClientRateLimit(options.RateLimit, options.Demo.Enabled);
if (options.Demo.Enabled)
{
    builder.WebHost.ConfigureKestrel(server =>
    {
        server.Limits.MaxConcurrentConnections = 100;
        server.Limits.MaxConcurrentUpgradedConnections = 30;
        server.Limits.MaxRequestBodySize = 32 * 1024;
        server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
    });
}

var app = builder.Build();

app.UseClientRateLimit(options.RateLimit);
if (options.Demo.Enabled)
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/media")) { await next(context); return; }
        // Multiple ranges are unnecessary for the player and can amplify work.
        var range = context.Request.Headers.Range.ToString();
        if (range.Length > 128 || range.Contains(','))
        {
            DemoMetrics.Reject("media_ranges");
            context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            return;
        }
        using var lease = context.RequestServices.GetRequiredService<DemoMediaBudget>()
            .Enter(ClientRateLimit.ClientKey(context.Connection.RemoteIpAddress));
        if (lease is null)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "5";
            return;
        }
        await next(context);
    });
}

// A visitor to the public demo arrives at the bare address; show them the TV, not the remote.
if (options.Demo.Enabled)
{
    app.Use((context, next) =>
    {
        if (context.Request.Path != "/") return next(context);
        context.Response.Redirect("/demo");
        return Task.CompletedTask;
    });
}

// The Angular build (the TV screen and the phone remote) is served from wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "on the air" }));
app.MapGet("/api/state", (TvService tv, string? room) =>
    tv.Snapshot(options.Demo.Enabled ? room ?? "" : TvService.HomeRoom) is { } snapshot
        ? Results.Ok(snapshot)
        : Results.NotFound());
app.MapGet("/api/picture", () => options.Picture);
app.MapGet("/api/demo", () => new DemoInfo(options.Demo.Enabled));
app.MapGet("/api/demo/credits", (TvService tv) => tv.Credits());
// Asked by the kiosk launcher at login, to know whether to start mpv or the browser.
app.MapGet("/api/player", () => new PlayerInfo(
    options.UsesMpv ? PlayerOptions.Mpv : PlayerOptions.Browser,
    options.Player.MpvSocket));
app.MapGet("/api/library", (TvService tv) => tv.Report());
// A rescan reads the whole media folder; on a public demo that is nobody's button to press.
app.MapPost("/api/rescan", async (TvService tv) =>
{
    if (options.Demo.Enabled) return Results.NotFound();
    await tv.Rescan();
    return Results.Ok(tv.Report());
});

// Episodes are addressed by id, never by path, and only scanned files resolve.
// Range support is what lets the screen jump straight into the middle of one.
app.MapMethods("/media/{id}", ["GET", "HEAD"], (string id, TvService tv, DemoMediaBudget budget) =>
{
    if (tv.FindMedia(id) is not { } path) return Results.NotFound();
    if (!options.Demo.Enabled) return Results.File(path, ContentTypeFor(path), enableRangeProcessing: true);
    return Results.Stream(new BudgetedMediaStream(File.OpenRead(path), budget), ContentTypeFor(path), enableRangeProcessing: true);
});

app.MapHub<TvHub>("/hub/tv");
app.MapChannelAdmin(options);
app.MapFallbackToFile("index.html");

app.Run();

static string ContentTypeFor(string path) =>
    Path.GetExtension(path).ToLowerInvariant() == ".webm" ? "video/webm" : "video/mp4";

public partial class Program;
