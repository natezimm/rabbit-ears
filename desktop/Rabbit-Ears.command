#!/bin/bash
# Finder and Terminal use the same launcher; arguments retain spaces and Unicode.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$root/launcher/RabbitEars.Desktop" "$@"
