# Getting the box working

A checklist from "Ubuntu is installed" to "the kids are watching". Work down it
in order; each step says where to do it and what you should see.

Wherever a command says `YOU@rabbitears.local`, put your own username and the box's
name. Step 1 prints exactly what to use.

Status: the app has only ever run on the laptop. None of this has been tried
on the real box yet, so expect a snag or two in steps 2 and 4.

## 1. Let the laptop reach the box

**On the box** (keyboard and mouse plugged in, logged in to the desktop).

- [ ] Connect it to your home network (Ethernet if you can; Wi-Fi works).
- [ ] Open Terminal and turn on remote login:

  ```bash
  sudo apt update && sudo apt install -y openssh-server
  ```

- [ ] Name the box `rabbitears`, so the `rabbitears.local` addresses in this
      checklist work as written. Ubuntu picks a name like
      `nathan-zimmerman.AL34`, and a name with a dot in it is not what the
      network announces, so the laptop cannot find it.

  ```bash
  sudo hostnamectl set-hostname rabbitears && sudo systemctl restart avahi-daemon
  ```

- [ ] Print the box's address and write it down:

  ```bash
  echo "$(whoami)@$(hostname).local"
  ```

**You should see** something like `nathan@rabbitears.local`. That is your
`YOU@rabbitears.local`.

- [ ] On the laptop, check it can reach the box: `ping -c 3 rabbitears.local`.
      If it cannot, use the box's number address instead (on the box,
      `hostname -I` prints it), for example `nathan@192.168.1.40`.

## 2. Send the app over and install it

**On the laptop**, in the project folder.

- [ ] Build, copy and install in one go, with the test shows:

  ```bash
  bash scripts/deploy.sh YOU@rabbitears.local --test-clips
  ```

  The first time, it asks whether to trust the box (type `yes`), then for your
  password on the box, then for it once more for `sudo`. Nothing appears while
  you type a password; that is normal.

**You should see** it end with `==> Rabbit Ears is installed.` and two
web addresses. If it prints a list headed `Still to do by hand`, do those in
step 3.

- [ ] From the laptop's browser, open `http://rabbitears.local:5080/api/library`.

**You should see** four test channels listed. If `rabbitears.local` does not load,
use the box's number address instead: on the box, `hostname -I` prints it.

## 3. Settings on the box

**On the box**, in Settings.

- [ ] **System > Users > Automatic Login**: on. (Unlock first, top right.)
- [ ] **Displays > Resolution**: 1920 x 1080, even on a 4K TV. The curved
      picture is drawn at screen resolution, and the box's graphics are modest.
- [ ] **Sound > Output Device**: the TV (HDMI).
- [ ] Anything from the installer's `Still to do by hand` list.
- [ ] Restart:

  ```bash
  sudo reboot
  ```

## 4. Check it works

- [ ] After the restart the TV comes up full screen **by itself**, on `CH 02`,
      with a show already playing and sound.
- [ ] Up and Down arrow keys change channel; the green `CH 03` banner appears
      top right.
- [ ] Right and Left arrows show the green volume bar and change the volume.
- [ ] `p` goes to standby (the picture collapses to a line) and `p` wakes it.
- [ ] The picture is smooth, not stuttering. This is the thing to watch.
- [ ] On your phone, on the same Wi-Fi, `http://rabbitears.local:5080` shows the
      remote, and its buttons work.

To get out of the TV screen, press the Windows key and open Terminal.

### If something is wrong

| What you see | Try this, on the box |
| --- | --- |
| Desktop, but no TV | Run `/opt/rabbitears/kiosk.sh` in Terminal and read what it says |
| TV page is black or says it cannot connect | `systemctl status rabbitears --no-pager` |
| Colour bars and `NO CHANNELS` | No shows found. Check `ls /srv/rabbitears/media` |
| Picture but no sound | Settings > Sound > Output Device |
| Black screen, and the keys do nothing | `journalctl -u rabbitears -n 20 --no-pager` should say `Connected to mpv`. If it says `Waiting for mpv`, log out and back in (or reboot) so the TV launcher starts mpv |
| `PRESS ANY KEY FOR SOUND` | Only with the browser as the TV. Press a key once. It means the browser was not started by the kiosk launcher |
| Picture stutters | Confirm step 3's 1080p setting. If it still stutters, add `Box__Picture__Crt__Enabled=false` to `/etc/rabbitears/box.env` and restart the service. That shows the plain video with no curved-glass effect; if it is smooth then, the effect is too much for the graphics chip |
| Login screen after a restart | Automatic Login (step 3) is not on |

The service's own log is the most useful thing to send when asking for help:

```bash
journalctl -u rabbitears -n 50 --no-pager
```

## 5. Put real shows on

**On the box.** Shows play exactly as you downloaded them (MKV, AVI, MP4 and
the rest); nothing is converted.

- [ ] Each folder in `/srv/rabbitears/media` is a channel, named after the
      folder. Move each show's folder in. Season subfolders are fine, and a
      folder of movies is one channel. On the same disk a move is instant:

  ```bash
  mv ~/Videos/Shows/Arthur /srv/rabbitears/media/
  ```

  From the laptop instead, copy the originals over. This can be re-run; it only
  sends what is new (if it says `rsync: command not found`, run
  `sudo apt install -y rsync` on the box):

  ```bash
  rsync -a --progress /path/to/shows/ YOU@rabbitears.local:/srv/rabbitears/media/
  ```

- [ ] Remove the test channels:

  ```bash
  cd /srv/rabbitears/media && rm -rf "Bars and Tone" "Game of Life" "Mandelbrot Hour" "Test Card" "Wide Load"
  ```

- [ ] Tell the box to look again (or wait ten minutes). If `curl` is missing,
      `sudo apt install -y curl`:

  ```bash
  curl -X POST http://localhost:5080/api/rescan
  ```

**You should see** your channels in the reply, numbered from 2 in A-to-Z order,
and the TV switches to them. The box has about 100 GB free; `df -h /srv` shows
what is left.

## 6. The remote

Needs the big-button remote and a Flirc USB receiver.

- [ ] Leave the remote **not** programmed for your TV's brand. If it is set to
      your TV, its volume and power buttons drive the real TV as well.
- [ ] Plug the Flirc into the **laptop** and install the Flirc app.
- [ ] In the app choose the **Full Keyboard** layout. Click a key on screen,
      then press the remote button that should send it. Map whichever of these
      your remote has; the first six are the ones that matter:

  | On-screen key | Remote button | Does |
  | --- | --- | --- |
  | Up arrow | Channel up | Next channel |
  | Down arrow | Channel down | Previous channel |
  | Right arrow | Volume up | Louder |
  | Left arrow | Volume down | Quieter |
  | `m` | Mute | Mute |
  | `p` | Power | Standby |
  | Enter | OK | Show the channel banner |
  | Backspace | Back | Previous channel watched |

- [ ] Move the Flirc to the **box**. If the box sits behind the TV, use a short
      USB extension so the Flirc faces the couch.
- [ ] Every remote button does what the table says, one step per press.

## 7. Make it kid-proof

- [ ] In the box's BIOS (usually `Del` or `F2` at power-on), set it to turn on
      when power returns: look for "Restore on AC Power Loss" and choose Power On.
- [ ] Pull the plug while a show is playing, then plug it back in. It should
      return to a playing channel by itself, with sound, and no keyboard.
- [ ] Volume down to 0, then once more: the screen says `GOODBYE` and the box
      shuts down. Plugging the power back in starts it again.
- [ ] Unplug the keyboard and mouse.

## Updating later

After changing the code on the laptop:

```bash
bash scripts/deploy.sh
```

It remembers the box's address, and keeps your settings and shows.

## Later, when you want them

- More room: an internal 2.5" SSD or a USB drive. Either needs a fixed mount
  the service can read, which the installer does not set up yet.
- An admin page to rename, reorder and hide channels from a browser.
- Settings for this box live in `/etc/rabbitears/box.env` (channel-change
  snow, resume or random, shutdown). After editing:
  `sudo systemctl restart rabbitears`.
