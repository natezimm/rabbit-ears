using RabbitEars.Api;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("Box").Get<BoxOptions>() ?? new BoxOptions();
// Relative paths in config are relative to the app folder, wherever it is started from.
options.MediaRoot = Path.GetFullPath(options.MediaRoot, builder.Environment.ContentRootPath);
options.StateDir = Path.GetFullPath(options.StateDir, builder.Environment.ContentRootPath);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<LibraryScanner>();
builder.Services.AddSingleton<StateFile>();
builder.Services.AddSingleton<TvService>();
builder.Services.AddHostedService(services => services.GetRequiredService<TvService>());
builder.Services.AddSignalR();

var app = builder.Build();

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
app.MapGet("/media/{id}", (string id, TvService tv) =>
    tv.FindMedia(id) is { } path
        ? Results.File(path, ContentTypeFor(path), enableRangeProcessing: true)
        : Results.NotFound());

app.MapHub<TvHub>("/hub/tv");
app.MapFallbackToFile("index.html");

app.Run();

static string ContentTypeFor(string path) =>
    Path.GetExtension(path).ToLowerInvariant() == ".webm" ? "video/webm" : "video/mp4";

public partial class Program;
