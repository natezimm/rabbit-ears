namespace RabbitEars.Api;

/// <summary>Keeps local Windows pipe identifiers out of filesystem path normalization.</summary>
public static class MpvEndpoint
{
    public const string PipePrefix = @"\\.\pipe\";

    public static string PipeName(string endpoint)
    {
        if (!endpoint.StartsWith(PipePrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(@"Windows mpv requires a local endpoint such as \\.\pipe\rabbitears.");
        var name = endpoint[PipePrefix.Length..];
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['\\', '/', ':']) >= 0)
            throw new ArgumentException("Use a nonempty local pipe name without directory separators.");
        return name;
    }

    public static string Normalize(string endpoint, string contentRoot, bool namedPipe) => namedPipe
        ? PipePrefix + PipeName(endpoint) : Path.GetFullPath(endpoint, contentRoot);
}
