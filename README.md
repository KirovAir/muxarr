<p align="center">
  <img src="docs/logo.png" alt="Muxarr" width="120"/><br/>
  <a href="https://github.com/KirovAir/muxarr/actions/workflows/build-and-deploy.yml"><img src="https://github.com/KirovAir/muxarr/actions/workflows/build-and-deploy.yml/badge.svg" alt="Build and Deploy"/></a>
  <a href="https://github.com/KirovAir/muxarr/pkgs/container/muxarr"><img src="https://img.shields.io/badge/ghcr.io-kirovair%2Fmuxarr-blue?logo=docker" alt="Docker Image"/></a>
  <a href="https://www.gnu.org/licenses/gpl-3.0"><img src="https://img.shields.io/badge/License-GPLv3-blue.svg" alt="License: GPL v3"/></a>
</p>

# Muxarr

Muxarr cleans up your media files by removing redundant audio and subtitle tracks and standardizing track metadata. It uses **mkvmerge** to remux files, so it only strips away what you don't need. Video, audio quality, and all remaining streams stay completely intact without quality loss since there is no re-encoding.

Integrates with Sonarr and Radarr for original language detection and automatic processing via webhooks.

> 🐉 Here be dragons. This project is still young, things may break.

## Features

- Strip redundant audio tracks (commentary, foreign dubs) and subtitles (SDH, foreign)
- Clean up track names by removing scene group tags and codec dumps
- Detect original language from Sonarr/Radarr metadata
- Per-directory profiles for language filtering and track handling
- Webhook support to automatically process new Sonarr/Radarr imports
- Pause, resume, and cancel running conversions
- Fixes metadata in-place when no tracks need removing
- Library overview with codec, resolution and language breakdowns

## Screenshots

<p align="center">
  <img src="docs/screenshots/before.png" alt="Before" width="600"/><br/>
  ⬇️<br/>
  <img src="docs/screenshots/after.png" alt="After" width="600"/><br/>
  <em>Before and after metadata cleanup</em>
</p>

<p align="center">
  <img src="docs/screenshots/dashboard.png" alt="Dashboard" width="800"/><br/>
  <em>Dashboard</em>
</p>

<p align="center">
  <img src="docs/screenshots/filedetails.png" alt="File Details" width="800"/><br/>
  <em>File details with track preview</em>
</p>

<p align="center">
  <img src="docs/screenshots/settings.png" alt="Profile Settings" width="800"/><br/>
  <em>Profile settings</em>
</p>

## Installation

### Docker Compose

```yaml
services:
  muxarr:
    image: ghcr.io/kirovair/muxarr:latest
    container_name: muxarr
    environment:
      - TZ=Europe/Amsterdam
      - PUID=1000
      - PGID=1000
    volumes:
      - /path/to/data:/data
      - /path/to/media:/media
    ports:
      - 8183:8183
    restart: unless-stopped
```

### Docker Run

```bash
docker run -d \
  --name=muxarr \
  -e TZ=Europe/Amsterdam \
  -e PUID=1000 \
  -e PGID=1000 \
  -p 8183:8183 \
  -v /path/to/data:/data \
  -v /path/to/media:/media \
  --restart unless-stopped \
  ghcr.io/kirovair/muxarr:latest
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|---|---|---|
| `TZ` | Timezone | `UTC` |
| `PUID` | User ID for file permissions | `1000` |
| `PGID` | Group ID for file permissions | `1000` |
| `ConnectionStrings__DefaultConnection` | SQLite connection string | `Data Source=/data/muxarr.db` |

### Volumes

| Path | Description |
|---|---|
| `/data` | Database and configuration |
| `/media` | Media files (use multiple `-v` mounts as needed) |

### Setup

1. Open `http://your-ip:8183`
2. Create a profile with your media directories and language rules
3. Optionally connect Sonarr/Radarr for original language detection
4. Scan and queue files for conversion

Make sure you properly setup a profile and convert your first files manually, remuxing can be quite fast on the right hardware. You don't want to clean too much. ;)

## Built With

- [.NET 9](https://dotnet.microsoft.com/) / Blazor
- [MKVToolNix](https://mkvtoolnix.download/) (mkvmerge, mkvpropedit)
- [FFmpeg](https://ffmpeg.org/)

## License

GPL-3.0 - see [LICENSE](LICENSE.md).

Muxarr is not affiliated with Sonarr, Radarr, or any other *arr projects.
