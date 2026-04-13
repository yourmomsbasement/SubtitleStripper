# Subtitle Stripper

A Jellyfin plugin that automatically removes embedded subtitle streams from your media files using ffmpeg. Optionally converts Blu-ray image-based (PGS) subtitles to `.srt` text sidecars via OCR before stripping, so the subtitle content is preserved.

## Features

- **Full library scan** — one-click scheduled task that processes every video in your library
- **Automatic processing** — optionally watches for newly added files and processes them on arrival
- **PGS → SRT conversion** — OCR converts Blu-ray image subtitles to text `.srt` sidecars before stripping (requires pgsrip + Tesseract)
- **Library scoping** — restrict processing to specific libraries
- **Dry run mode** — logs what would happen without touching any files
- **Backup option** — keeps a `.bak` copy of the original file
- **Language filter** — only OCR specific languages (e.g. `en`, `fr`, `pt-BR`)

## Installation

### Via Jellyfin Plugin Repository (recommended)

1. Open your Jellyfin dashboard
2. Go to **Administration → Plugins → Repositories**
3. Click **+** and add this URL:
   ```
   https://raw.githubusercontent.com/yourmomsbasement/SubtitleStripper/main/manifest.json
   ```
4. Go to **Catalog**, find **Subtitle Stripper**, and click **Install**
5. Restart Jellyfin

### Manual Installation

1. Download the latest `SubtitleStripper_x.x.x.zip` from the [Releases](https://github.com/yourmomsbasement/SubtitleStripper/releases) page
2. Extract `Jellyfin.Plugin.SubtitleStripper.dll` into your Jellyfin plugins folder:
   ```
   <jellyfin-config>/plugins/SubtitleStripper_1.0.0/
   ```
3. Restart Jellyfin

> **Docker users:** the plugins folder is typically inside your config volume, e.g.
> `/config/plugins/SubtitleStripper_1.0.0/`

## Configuration

Go to **Administration → Plugins → Subtitle Stripper**.

| Setting | Description |
|---|---|
| **Dry run** | Log what would happen without modifying any files |
| **Keep backup** | Save a `.bak` copy of the original file after stripping |
| **Process on item added** | Automatically process files when added to your library |
| **ffmpeg path override** | Use a specific ffmpeg binary instead of Jellyfin's built-in one |
| **Library scope** | Restrict processing to selected libraries (leave all unchecked for all libraries) |
| **Convert PGS to SRT** | OCR Blu-ray image subtitles to `.srt` sidecars before stripping |
| **pgsrip executable path** | Path to the pgsrip executable or wrapper script |
| **Fallback OCR language** | Tesseract language code used when a stream has no language tag (e.g. `eng`) |
| **Languages to convert** | IETF codes to OCR (e.g. `en,fr`). Leave empty to convert all languages |

## Running a Scan

After configuring the plugin:

1. Go to **Administration → Scheduled Tasks**
2. Find **Strip Subtitles — Full Library Scan**
3. Click the run button

New files added after the scan will be processed automatically if **Process on item added** is enabled.

## PGS → SRT (Optional)

PGS subtitles are image-based tracks found on Blu-ray rips (`.mkv` files). Without conversion, stripping them destroys the subtitle content. With this option enabled, the plugin runs OCR on each PGS track and saves it as a `.srt` text file next to the video before stripping.

### Requirements

- [pgsrip](https://github.com/ratoaq2/pgsrip) — `pip install pgsrip`
- [Tesseract OCR](https://github.com/tesseract-ocr/tesseract)

### Docker Jellyfin

The Jellyfin Docker container has no Python runtime. You need to install pgsrip into your persistent config volume so it survives container restarts.

**Option A — bare metal / system Python available:**
```bash
pip install pgsrip
```
Set **pgsrip executable path** to the output of `which pgsrip`, or leave blank if it's on PATH.

**Option B — Docker with no Python (e.g. linuxserver/jellyfin on TrueNAS SCALE):**

Run the following from a shell with access to your Jellyfin config volume. This installs a portable Python + pgsrip into `/config/pgsrip/` which persists across container restarts.

```bash
# Spin up a temporary Debian container with your Jellyfin config volume mounted
docker run --rm -it \
  -v /path/to/jellyfin/config:/config \
  debian:trixie bash

# Inside the container:
apt-get update && apt-get install -y python3 python3-pip tesseract-ocr
pip install pgsrip --break-system-packages --target /config/pgsrip/pylib

# Copy the Python runtime into the config volume
cp $(which python3) /config/pgsrip/
cp -r /usr/lib/python3* /config/pgsrip/pylib/
cp /usr/lib/x86_64-linux-gnu/lib*.so* /config/pgsrip/ 2>/dev/null || true

# Create a wrapper script
mkdir -p /config/pgsrip/bin
cat > /config/pgsrip/bin/pgsrip-run.sh << 'EOF'
#!/bin/sh
export PYTHONPATH=/config/pgsrip/pylib
export PYTHONHOME=/config/pgsrip/pylib
export LD_LIBRARY_PATH=/config/pgsrip
exec /config/pgsrip/python3 /config/pgsrip/pylib/bin/pgsrip "$@"
EOF
chmod +x /config/pgsrip/bin/pgsrip-run.sh
exit
```

Then set **pgsrip executable path** in the plugin to `/config/pgsrip/bin/pgsrip-run.sh`.

### Language Codes

Use IETF format for the language filter — `en`, `fr`, `de`, `pt-BR` — not ISO 639-2 (`eng`, `fra`). Leave the field empty to convert all languages.

## Supported File Types

`.mkv`, `.mp4`, `.m4v`, `.avi`, `.mov`

## Building from Source

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
dotnet build -c Release
```

To build and package a release ZIP:
```powershell
.\package.ps1 -Version 1.0.0
```

## Credits

PGS → SRT conversion is powered by [pgsrip](https://github.com/ratoaq2/pgsrip) by [@ratoaq2](https://github.com/ratoaq2) — a fantastic tool for OCR-converting Blu-ray image subtitles to text. This plugin would not have PGS support without it.

OCR engine provided by [Tesseract](https://github.com/tesseract-ocr/tesseract).

Subtitle stripping and stream detection handled by [ffmpeg](https://ffmpeg.org/).

## License

MIT
