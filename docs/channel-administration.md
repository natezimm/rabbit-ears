# Manage the home lineup

Open `http://rabbitears.local:5080/admin`, or choose **Manage channels** below
the phone remote's channel list. This page is available only on the home box;
the public demo refuses all channel management API calls.

Each show folder remains the permanent identity of a channel. Change its
display name here instead of renaming the folder, which would change both
the channel identity and episode IDs. Existing saved positions remain compatible.

- Edit a display name and optional channel number (1–999). Numbers determine
  the dial order. Blank numbers fill available slots starting at 2. Reserved
  custom numbers are not assigned to automatic channels.
- Turn **On the dial** off to hide a channel without deleting any media.
- Choose **Default**, **Live**, **Resume**, or **Random**. Default follows the
  global `Box:Tv:TuneIn` setting. Modes take effect on the next tune;
  saving does not interrupt an eligible episode already playing.
- Expand **Choose episodes**. Uncheck whole season folders or individual files.
  A channel with no eligible episodes disappears from the dial; it remains in
  the editor so it can be restored. Season folders are relative directory paths,
  not season numbers inferred from filenames.
- Choose **Save lineup** to apply all edits. Connected TV and remote clients
  receive the new lineup automatically. If the current video is excluded, the
  box picks another eligible video; an empty lineup shows NO CHANNELS.
- Choose **Rescan folders** after copying files. Save or discard unsaved edits
  first. Missing source folders keep their settings for when the drive returns.

Settings are saved atomically in `/var/lib/rabbitears/channels.json` (or the
configured `Box:StateDir`). Back up this file together with `state.json`.
An invalid or unreadable settings file leaves folder-based playback available,
but blocks editing rather than overwriting the damaged document. Repair or
restore the file and restart the service. The service user must be able to
write to the state directory.

If another tab saves first, a stale save returns a conflict. Your edits stay on
screen; **Discard edits & reload** fetches the current lineup. Write requests
require `X-RabbitEars-Admin: 1` and refuse a foreign Origin. This protects against
cross-site browser requests; it is not authentication. Keep the home service on
your trusted LAN, as before.

For scripted management, GET `/api/admin/channels`, modify the `settings`
records, and PUT `{ "version": "<the returned version>", "channels": [...] }`
to the same URL with JSON content type and the admin header. POST
`/api/admin/rescan` uses the same header. There are no media deletion/upload APIs.
