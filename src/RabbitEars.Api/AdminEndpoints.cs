namespace RabbitEars.Api;

/// <summary>Management belongs only to the trusted home network, never the public demo.</summary>
public static class AdminEndpoints
{
    public static void MapChannelAdmin(this WebApplication app, BoxOptions options)
    {
        app.MapGet("/api/admin/status", () => new { enabled = !options.Demo.Enabled });
        if (options.Demo.Enabled)
        {
            app.Map("/api/admin/{**path}", () => Results.NotFound());
            return;
        }

        var admin = app.MapGroup("/api/admin");
        admin.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            if (!HttpMethods.IsGet(http.Request.Method))
            {
                // HTML forms and cross-origin scripts cannot set this header without
                // a CORS preflight. There is deliberately no CORS policy on the box.
                if (http.Request.Headers["X-RabbitEars-Admin"] != "1")
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                var origin = http.Request.Headers.Origin.ToString();
                if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                    || uri.GetLeftPart(UriPartial.Authority) != $"{http.Request.Scheme}://{http.Request.Host}"))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            try { return await next(context); }
            catch (CatalogConflictException) { return Results.Conflict(new { error = "The lineup changed in another tab. Reload before saving." }); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                app.Logger.LogError(ex, "Channel metadata operation failed.");
                return Results.Problem("Could not read or save channels.json. Check the service log and state directory permissions.", statusCode: 503);
            }
        });
        admin.MapGet("/channels", async (TvService tv) => Results.Ok(await tv.AdminView()));
        admin.MapPut("/channels", async (CatalogDocument request, TvService tv) => Results.Ok(await tv.EditChannels(request)));
        admin.MapPost("/rescan", async (TvService tv) =>
        {
            await tv.Rescan();
            return Results.Ok(await tv.AdminView());
        });
    }
}
