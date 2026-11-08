using RabbitEars.Api;

namespace RabbitEars.Tests;

public sealed class MpvEndpointTests
{
    [Fact]
    public void A_pipe_is_not_normalized_as_a_file_path()
    {
        const string endpoint = @"\\.\pipe\rabbitears-test";
        Assert.Equal(endpoint, MpvEndpoint.Normalize(endpoint, Path.GetTempPath(), true));
        Assert.Equal("rabbitears-test", MpvEndpoint.PipeName(endpoint));
    }

    [Theory]
    [InlineData(@"\\remote\pipe\test")]
    [InlineData(@"\\.\pipe\")]
    [InlineData(@"\\.\pipe\nested\name")]
    [InlineData("/tmp/mpv.sock")]
    public void Windows_requires_a_local_named_pipe(string endpoint) =>
        Assert.Throws<ArgumentException>(() => MpvEndpoint.PipeName(endpoint));

    [Fact]
    public void Unix_paths_keep_their_existing_resolution() =>
        Assert.Equal(Path.Combine(Path.GetTempPath(), "mpv.sock"),
            MpvEndpoint.Normalize("mpv.sock", Path.GetTempPath(), false));
}
