#!/usr/bin/env python3
"""Exercise a published Mac package with real mpv/ffprobe and no window or speakers."""
import json
import os
from pathlib import Path
import shlex
import shutil
import signal
import socket
import subprocess
import sys
import tempfile
import time
import urllib.request


def eventually(action, timeout=60):
    deadline = time.monotonic() + timeout
    while True:
        try:
            result = action()
            if result:
                return result
        except (OSError, ValueError, KeyError):
            pass
        if time.monotonic() >= deadline:
            raise AssertionError("Timed out waiting for desktop session")
        time.sleep(0.2)


def main():
    package = Path(sys.argv[1]).resolve()
    with tempfile.TemporaryDirectory(prefix="ears-mac-") as temporary:
        root = Path(temporary)
        media = root / "TV shows 日本語" / "Cartoons"
        media.mkdir(parents=True)
        episode = media / "Test episode.mp4"
        subprocess.run(["ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=10",
                        "-f", "lavfi", "-i", "sine=frequency=440", "-t", "30", "-c:v", "mpeg4",
                        "-c:a", "aac", str(episode)], check=True)
        mpv = shutil.which("mpv")
        assert mpv, "mpv must be installed"
        wrapper = root / "headless mpv"
        wrapper.write_text("#!/bin/sh\nexec " + shlex.quote(mpv)
                           + ' "$@" --vo=null --ao=null --hwdec=no --force-window=no\n')
        wrapper.chmod(0o700)
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            port = listener.getsockname()[1]
        base = f"http://127.0.0.1:{port}"
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

        def get(path):
            with opener.open(base + path, timeout=2) as response:
                return json.load(response)

        def command(endpoint, arguments, reply=True):
            with socket.socket(socket.AF_UNIX) as client:
                client.settimeout(3)
                client.connect(endpoint)
                client.sendall(json.dumps({"command": arguments, "request_id": 42}).encode() + b"\n")
                if not reply:
                    return None
                with client.makefile("rb") as stream:
                    for line in stream:
                        message = json.loads(line)
                        if message.get("request_id") == 42:
                            if message["error"] == "property unavailable":
                                return None
                            assert message["error"] == "success", message
                            return message.get("data")
            raise AssertionError("mpv disconnected before its reply")

        arguments = [str(package / "Rabbit-Ears.command"), "--media", str(media.parent),
                     "--state", str(root / "state"), "--port", str(port), "--mpv", str(wrapper),
                     "--ffprobe", shutil.which("ffprobe"), "--local"]
        process = None
        try:
            for attempt in range(2):
                process = subprocess.Popen(arguments)
                endpoint = eventually(lambda: get("/api/player")["socket"])
                eventually(lambda: Path(endpoint).exists())
                eventually(lambda: command(endpoint, ["get_property", "path"]) == str(episode))
                eventually(lambda: (command(endpoint, ["get_property", "time-pos"]) or 0) > 0)
                assert get("/api/demo") == {"enabled": False}
                assert get("/api/library")["episodes"] == 1
                if attempt == 0:
                    duplicate = subprocess.run(arguments, capture_output=True, text=True, timeout=15)
                    assert duplicate.returncode != 0 and "lock profile" in duplicate.stderr, duplicate.stderr
                    collision_args = arguments.copy()
                    collision_args[collision_args.index("--state") + 1] = str(root / "other state")
                    collision = subprocess.run(collision_args, capture_output=True, text=True, timeout=15)
                    assert collision.returncode != 0 and "Port" in collision.stderr, collision.stderr
                    original_volume = get("/api/state")["volume"]
                    command(endpoint, ["script-message", "ears", "volume-up"])
                    def raised_volume():
                        volume = get("/api/state")["volume"]
                        return volume if volume > original_volume else None
                    saved_volume = eventually(raised_volume)
                    command(endpoint, ["quit", 0], reply=False)
                    assert process.wait(timeout=20) == 0
                    assert (root / "state" / "state.json").exists()
                else:
                    assert get("/api/state")["volume"] == saved_volume
                    # A crashed player must also stop its API; no relaunch loop.
                    os.kill(command(endpoint, ["get_property", "pid"]), signal.SIGKILL)
                    assert process.wait(timeout=20) != 0
                process = None
                assert not Path(endpoint).parent.exists(), "Socket directory was not removed"
                with socket.socket() as probe:
                    assert probe.connect_ex(("127.0.0.1", port)) != 0, "API listener survived exit"
            print("Mac package: playback, duplicate/port rejection, persistence and cleanup passed")
        finally:
            if process is not None:
                process.terminate()
                try:
                    process.wait(timeout=20)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
            for log in (root / "state" / "logs").glob("*.log"):
                print(f"--- {log.name} ---\n{log.read_text()[-6000:]}")


if __name__ == "__main__":
    main()
