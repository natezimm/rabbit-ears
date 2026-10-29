# Using the box

How to drive Rabbit Ears day to day: the remote, how folders become channels,
and the settings worth knowing. To set a box up from scratch, see
[box setup](box-setup.md).

## The remote

The TV page takes plain key presses, which is what a Flirc USB receiver sends.
Program the Flirc with its **Full Keyboard** profile:

| Key | Remote button | What it does |
| --- | --- | --- |
| Up / Down | Channel up / down | Next / previous channel, wrapping around |
| Right / Left | Volume up / down | One step per press; held buttons do not repeat |
| `m` | Mute | Mute |
| `p` | Power | Standby (the picture collapses to a line, like a tube switching off) |
| `0`–`9` | Number keys | Tune straight to a channel; Enter/OK skips the short wait |
| `i` | Info | Flash the channel banner |
| `l` | Last | Jump back to the previous channel |

Volume-down once more at 0 says `GOODBYE` and shuts the box down cleanly when
`PowerOffAtZeroVolume` is on (it is in the mini PC config), so it is safe to pull
the plug.

The phone remote at `/remote` has the same buttons plus a channel list. It and
the TV both just draw what the box says is on, so they cannot disagree.

At home, choose **Manage channels** on the remote or open `/admin` to rename,
number, hide, and filter channels and set each channel's tune-in behavior.
See [channel administration](channel-administration.md). Metadata is saved
separately from media folders; renaming a display label preserves playback.

## Shows and movies

One folder per channel; the folder name is the channel name. Season subfolders
are fine. Channels are numbered from 2 in A-to-Z order.

```text
/srv/rabbitears/media/
  Arthur/                 -> CH 02  Arthur
    Season 1/S01E01.mp4
  Disney Movies/          -> CH 03  Disney Movies
    The Lion King.mp4
    Aladdin.mp4
  Dragon Tales/           -> CH 04  Dragon Tales
    S01E01.mp4
```

A folder of movies is a channel like any other: each film takes its turn in the
shuffle. With the default `Live` setting every channel keeps broadcasting while
you watch another one, like real TV: flip away for five minutes and the show is
five minutes further on when you come back, or onto its next episode. `Resume`
instead picks up exactly where you left it, which some prefer for films.

On the box, mpv plays the files as they are: MKV, AVI, MP4, MPEG and the other
common rip formats, with no converting. The browser TV page (the laptop and the
demo site) only plays H.264 + AAC in MP4, or WebM, and `GET /api/library` lists
any files it cannot play. To make a library the browser can play too, convert
it once, keeping its folder layout:

```bash
bash scripts/convert-media.sh /path/to/originals /path/to/converted
```

New episodes and new show folders are picked up within ten minutes, or at once
with `curl -X POST http://localhost:5080/api/rescan`.

## Settings

Everything is in the `Box` section of
[`src/RabbitEars.Api/appsettings.json`](../src/RabbitEars.Api/appsettings.json).
On the mini PC, override settings in `/etc/rabbitears/box.env` instead (names
joined with two underscores) and restart the service. The ones worth knowing:

| Setting | Default | |
| --- | --- | --- |
| `Tv.TuneIn` | `Live` | `Live`: channels keep running while you are away, rolling into later episodes. `Resume`: a channel picks up exactly where you left it. `Random`: every flip starts a fresh shuffled episode. |
| `Tv.StartOffsetMin` / `Max` | `6` / `10` | A new episode starts this many seconds in, so it feels already on. |
| `Picture.Transition` | `none` | Channel-change effect: `none`, `static` (analog snow) or `glitch`. |
| `Picture.Crt.*` | see the file | `Curvature`, `CornerRadius`, `Vignette`, `ScanlineIntensity`. `Enabled: false` shows the plain video with no shader at all, the lightest option for a weak graphics chip. |
| `Picture.Osd.Color` | `#3DFF6E` | The phosphor green. |
| `Tv.InitialVolume` / `VolumeStep` | `60` / `5` | |
| `Ffprobe` | `ffprobe` | Reads each episode's length from its file header so `Live` channels know when to roll to the next episode. Without it, lengths are learned as episodes play. |
| `Player.Kind` | `mpv` | What draws the box's screen: `mpv` plays any format, `browser` is the `/tv` page in Chromium (MP4 and WebM only). Takes effect at the next login. |

