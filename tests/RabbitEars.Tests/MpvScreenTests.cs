using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitEars.Api;
using RabbitEars.Core;

namespace RabbitEars.Tests;

public sealed class MpvScreenTests : IDisposable
{
    private readonly string _media = Directory.CreateTempSubdirectory("rabbitears-media-").FullName;
    private readonly string _state = Directory.CreateTempSubdirectory("rabbitears-state-").FullName;
    private readonly FakeClock _clock = new();
    private readonly Queue<TvSnapshot> _changes = [];
    private readonly FakeMpv _mpv = new();

    public MpvScreenTests()
    {
        Touch("Arthur/S01E01.mkv");
        Touch("Arthur/S01E02.mkv");
        Touch("Dragon Tales/S01E01.avi");
    }

    public void Dispose()
    {
        Directory.Delete(_media, recursive: true);
        Directory.Delete(_state, recursive: true);
    }

    private void Touch(string relativePath)
    {
        var path = Path.Combine(_media, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    private (TvService Tv, MpvScreen Screen) Box(PictureOptions? picture = null)
    {
        var options = new BoxOptions
        {
            MediaRoot = _media,
            StateDir = _state,
            RescanMinutes = 0,
            Picture = picture ?? new PictureOptions(),
        };
        var tv = new TvService(
            options,
            new LibraryScanner(options, NullLogger<LibraryScanner>.Instance),
            new StateFile(options, NullLogger<StateFile>.Instance),
            new RecordingHub(),
            NullLogger<TvService>.Instance,
            _clock);
        // MpvPlayer queues these and feeds them back one at a time; Settle does the same.
        tv.HomeChanged += _changes.Enqueue;
        return (tv, new MpvScreen(options, tv, NullLogger<MpvScreen>.Instance, _clock));
    }

    private async Task Settle(MpvScreen screen)
    {
        while (_changes.TryDequeue(out var snapshot)) await screen.Show(snapshot);
    }

    private async Task Press(MpvScreen screen, params string[] args)
    {
        await screen.Hear(Event(new { @event = "client-message", args = (string[])["ears", .. args] }));
        await Settle(screen);
    }

    private static Task Started(MpvScreen screen, long entry) =>
        screen.Hear(Event(new { @event = "start-file", playlist_entry_id = entry }));

    private static Task Ended(MpvScreen screen, long entry, string reason) =>
        screen.Hear(Event(new { @event = "end-file", reason, playlist_entry_id = entry }));

    private static JsonElement Event(object body) => JsonSerializer.SerializeToElement(body);

    [Fact]
    public async Task Connecting_puts_the_current_channel_on_from_its_start_point()
    {
        var (tv, screen) = Box();

        await screen.Attach(_mpv);

        var playing = tv.Snapshot().Playing!;
        var load = Assert.Single(_mpv.Loads);
        Assert.Equal(tv.FindMedia(playing.EpisodeId), load.Url);
        Assert.Equal("replace", load.Flags);
        Assert.Equal(playing.Start, load.Start, 3);
    }

    [Fact]
    public async Task An_episode_that_plays_to_the_end_rolls_on_to_the_next()
    {
        var (tv, screen) = Box();
        await screen.Attach(_mpv);
        var first = tv.Snapshot().Playing!;
        var entry = _mpv.Loads[^1].Entry;

        await Started(screen, entry);
        await Ended(screen, entry, "eof");
        await Settle(screen);

        var next = tv.Snapshot().Playing!;
        Assert.NotEqual(first.Token, next.Token);
        Assert.Equal(PlayReason.Advance, next.Reason);
        Assert.Equal(tv.FindMedia(next.EpisodeId), _mpv.Loads[^1].Url);
    }

    [Fact]
    public async Task A_file_mpv_cannot_open_is_skipped()
    {
        var (tv, screen) = Box();
        await screen.Attach(_mpv);
        var first = tv.Snapshot().Playing!;
        var entry = _mpv.Loads[^1].Entry;

        await Ended(screen, entry, "error");
        await Settle(screen);

        Assert.NotEqual(first.Token, tv.Snapshot().Playing!.Token);
        Assert.Equal(2, _mpv.Loads.Count);
    }

    [Fact]
    public async Task The_end_of_a_file_that_was_replaced_changes_nothing()
    {
        var (tv, screen) = Box();
        await screen.Attach(_mpv);
        var arthur = _mpv.Loads[^1].Entry;
        await Press(screen, "channel-up");
        var dragonTales = tv.Snapshot();

        await Ended(screen, arthur, "eof");

        Assert.Equal(dragonTales.Revision, tv.Snapshot().Revision);
    }

    [Fact]
    public async Task Remote_keys_heard_by_mpv_change_the_channel()
    {
        var (tv, screen) = Box();
        await screen.Attach(_mpv);

        await Press(screen, "channel-up");

        Assert.Equal(new ChannelInfo(3, "Dragon Tales"), tv.Snapshot().Channel);
        Assert.Equal(Path.Combine(_media, "Dragon Tales", "S01E01.avi"), _mpv.Loads[^1].Url);
        Assert.Equal("replace", _mpv.Loads[^1].Flags);
    }

    [Fact]
    public void Every_key_in_input_conf_is_one_the_screen_knows()
    {
        var conf = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "mpv", "input.conf"))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        Assert.All(conf, words =>
        {
            Assert.Equal(["script-message", "ears"], words[1..3]);
            if (words[3] == "digit") Assert.Matches("^[0-9]$", words[4]);
            else Assert.Contains(words[3], MpvScreen.Keys.Keys);
        });
        // The Flirc layout in docs/box-setup.md.
        var bound = conf.Select(words => words[0]).ToHashSet();
        Assert.Superset(new HashSet<string> { "UP", "DOWN", "RIGHT", "LEFT", "m", "p", "ENTER", "BS" }, bound);
    }

    [Fact]
    public async Task Volume_is_set_in_mpv_at_the_same_loudness_as_the_browser()
    {
        var (tv, screen) = Box();
        await screen.Attach(_mpv);

        await Press(screen, "volume-up");

        var volume = tv.Snapshot().Volume;
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"[\"set_property\",\"volume\",{MpvScreen.MpvVolume(volume)}]"), _mpv.Commands);
        // The browser squares the level; mpv cubes its volume.
        foreach (var level in new[] { 0, 5, 40, 60, 100 })
        {
            Assert.Equal(Math.Pow(level / 100.0, 2), Math.Pow(MpvScreen.MpvVolume(level) / 100, 3), tolerance: 0.001);
        }
    }

    [Fact]
    public async Task Without_a_transition_a_channel_change_cuts_straight_to_the_next_file()
    {
        var (_, screen) = Box();
        await screen.Attach(_mpv);

        await Press(screen, "channel-up");

        Assert.Equal(2, _mpv.Loads.Count);
        Assert.DoesNotContain(_mpv.Loads, load => load.Url.StartsWith("av://"));
    }

    [Fact]
    public async Task With_static_a_channel_change_plays_snow_while_the_next_file_opens()
    {
        var (tv, screen) = Box(new PictureOptions { Transition = "static", TransitionSeconds = 0.4 });
        await screen.Attach(_mpv);

        await Press(screen, "channel-up");

        var (snow, show) = (_mpv.Loads[^2], _mpv.Loads[^1]);
        Assert.StartsWith("av://lavfi:nullsrc", snow.Url);
        Assert.Contains("d=0.4,", snow.Url);
        Assert.Equal("replace", snow.Flags);
        Assert.Equal(tv.FindMedia(tv.Snapshot().Playing!.EpisodeId), show.Url);
        Assert.Equal("append", show.Flags);

        // The snow ending is not the episode ending.
        await Started(screen, snow.Entry);
        var before = tv.Snapshot().Revision;
        await Ended(screen, snow.Entry, "eof");
        Assert.Equal(before, tv.Snapshot().Revision);
    }

    [Fact]
    public async Task Coming_back_to_a_channel_resumes_where_it_was_left()
    {
        var (_, screen) = Box();
        await screen.Attach(_mpv);
        await Started(screen, _mpv.Loads[^1].Entry);
        _mpv.TimePos = 123.4;

        await Press(screen, "channel-up");
        await Press(screen, "last-channel");

        Assert.Equal(123.4, _mpv.Loads[^1].Start, 3);
    }

    [Fact]
    public async Task A_restarted_mpv_picks_up_where_the_episode_had_got_to()
    {
        var (_, screen) = Box();
        await screen.Attach(_mpv);
        await Started(screen, _mpv.Loads[^1].Entry);
        _mpv.TimePos = 200;
        _clock.Advance(TimeSpan.FromSeconds(3));
        await screen.Tick();

        var restarted = new FakeMpv();
        await screen.Attach(restarted);

        Assert.Equal(200, Assert.Single(restarted.Loads).Start, 3);
    }

    [Fact]
    public async Task After_the_service_restarts_mpv_carries_on_with_what_it_was_already_showing()
    {
        var (tv, screen) = Box();
        var playing = tv.Snapshot().Playing!;
        Assert.Equal(PlayReason.Wake, playing.Reason);
        _mpv.Already = (tv.FindMedia(playing.EpisodeId)!, playing.Start + 5, Entry: 7);

        await screen.Attach(_mpv);

        Assert.Empty(_mpv.Loads);
        await Ended(screen, 7, "eof");
        Assert.NotEqual(playing.Token, tv.Snapshot().Playing!.Token);
    }

    [Fact]
    public async Task Playback_that_stops_moving_is_treated_as_a_dead_file()
    {
        var (tv, screen) = Box();
        await screen.Attach(_mpv);
        var stuck = tv.Snapshot().Playing!;
        await Started(screen, _mpv.Loads[^1].Entry);
        _mpv.TimePos = 50;

        for (var report = 0; report < 8; report++)
        {
            _clock.Advance(TimeSpan.FromSeconds(2.1));
            await screen.Tick();
        }

        Assert.NotEqual(stuck.Token, tv.Snapshot().Playing!.Token);
    }

    [Fact]
    public async Task Standby_stops_mpv_and_says_so_and_waking_plays_again()
    {
        var (tv, screen) = Box();
        await screen.Attach(_mpv);

        await Press(screen, "power");

        Assert.Contains("[\"stop\"]", _mpv.Commands);
        Assert.Contains("STANDBY", _mpv.Osd);

        await Press(screen, "power");

        Assert.Equal(tv.FindMedia(tv.Snapshot().Playing!.EpisodeId), _mpv.Loads[^1].Url);
        Assert.DoesNotContain("STANDBY", _mpv.Osd);
    }

    [Fact]
    public async Task With_no_channels_mpv_shows_colour_bars()
    {
        var (tv, screen) = Box();
        await screen.Attach(_mpv);
        foreach (var folder in Directory.GetDirectories(_media)) Directory.Delete(folder, recursive: true);

        await tv.Rescan();
        await Settle(screen);

        Assert.StartsWith("av://lavfi:smptebars", _mpv.Loads[^1].Url);
    }

    [Fact]
    public async Task The_channel_banner_shows_on_a_channel_change_and_fades()
    {
        var (_, screen) = Box(new PictureOptions { BannerSeconds = 4 });
        await screen.Attach(_mpv);

        await Press(screen, "channel-up");
        Assert.Contains("CH 03", _mpv.Osd);
        Assert.Contains("Dragon Tales", _mpv.Osd);

        _clock.Advance(TimeSpan.FromSeconds(4.1));
        await screen.Tick();
        Assert.Equal("", _mpv.Osd);
    }

    [Fact]
    public void The_tube_shader_is_written_with_this_boxs_settings()
    {
        var template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mpv", "crt.glsl"));

        var baked = MpvScreen.BakeShader(template, new CrtOptions { Curvature = 0.2, Scanlines = false });

        Assert.Contains("#define CURVATURE 0.2\n", baked);
        Assert.Contains("#define SCANLINES 0.0\n", baked);
        Assert.Contains("#define VIGNETTE 0.3\n", baked);
        Assert.StartsWith("//", baked);
        Assert.Contains("//!HOOK OUTPUT", baked);
    }

    /// <summary>
    /// Stands in for mpv's control socket. Records every command as JSON,
    /// numbers each loaded file as mpv does, and answers the few questions the
    /// screen asks.
    /// </summary>
    private sealed class FakeMpv : IMpvLink
    {
        private long _lastEntry;

        public List<string> Commands { get; } = [];
        public List<(string Url, string Flags, double Start, long Entry)> Loads { get; } = [];
        public string Osd { get; private set; } = "";
        public double? TimePos { get; set; }

        /// <summary>A file mpv was already playing when the screen connected.</summary>
        public (string Path, double Position, long Entry)? Already { get; set; }

        public Task<JsonElement?> Request(object command)
        {
            var json = JsonSerializer.Serialize(command);
            Commands.Add(json);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                var url = root.GetProperty("url").GetString()!;
                var start = root.TryGetProperty("options", out var options)
                    ? double.Parse(options.GetProperty("start").GetString()!, CultureInfo.InvariantCulture)
                    : 0;
                var entry = ++_lastEntry;
                Loads.Add((url, root.GetProperty("flags").GetString()!, start, entry));
                return Answer(new { playlist_entry_id = entry });
            }

            var args = root.EnumerateArray().Select(a => a.ToString()).ToArray();
            return args switch
            {
                ["osd-overlay", _, "none", ..] => Draw(""),
                ["osd-overlay", _, "ass-events", var ass, ..] => Draw(ass),
                ["get_property", "path"] when Already is { } a => Answer(a.Path),
                ["get_property", "time-pos"] when Already is { } a && TimePos is null => Answer(a.Position),
                ["get_property", "time-pos"] => TimePos is { } t ? Answer(t) : Refuse(),
                ["get_property", "playlist-playing-pos"] when Already is not null => Answer(0),
                ["get_property", "playlist/0/id"] when Already is { } a => Answer(a.Entry),
                ["get_property", _] => Refuse(),
                _ => Task.FromResult<JsonElement?>(default(JsonElement)),
            };
        }

        private Task<JsonElement?> Draw(string ass)
        {
            Osd = ass;
            return Task.FromResult<JsonElement?>(default(JsonElement));
        }

        private static Task<JsonElement?> Answer(object data) =>
            Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(data));

        private static Task<JsonElement?> Refuse() => Task.FromResult<JsonElement?>(null);
    }
}
