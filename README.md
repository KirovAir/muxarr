<p align="center">
  <img src="docs/logo.png" alt="Muxarr" width="120"/><br/>
  <a href="https://github.com/KirovAir/muxarr/actions/workflows/build-and-deploy.yml"><img src="https://github.com/KirovAir/muxarr/actions/workflows/build-and-deploy.yml/badge.svg" alt="Build and Deploy"/></a>
  <a href="https://github.com/KirovAir/muxarr/pkgs/container/muxarr"><img src="https://img.shields.io/badge/ghcr.io-kirovair%2Fmuxarr-blue?logo=docker" alt="Docker Image"/></a>
  <a href="https://github.com/KirovAir/muxarr/stargazers"><img src="https://img.shields.io/github/stars/KirovAir/muxarr?style=flat" alt="GitHub Stars"/></a>
  <a href="https://www.gnu.org/licenses/gpl-3.0"><img src="https://img.shields.io/badge/License-GPLv3-blue.svg" alt="License: GPL v3"/></a>
</p>

# Muxarr

*Ever had your player pick the wrong audio language, or show 20 subtitle options you'll never use? Most media files ship with far more tracks than you need.*

Muxarr cleans them up by removing unwanted audio and subtitle tracks and standardizing track metadata. It uses **mkvmerge** for MKV files and **ffmpeg** with stream copy for other containers, so tracks are remuxed rather than re-encoded and there is zero quality loss. A 4GB file takes about a minute instead of hours, even on low-end hardware like a NAS or Raspberry Pi.

**Hooks into Sonarr & Radarr** for original language detection and automatic processing - new imports get cleaned up as they arrive.

<p align="center">
  <img src="https://raw.githubusercontent.com/KirovAir/muxarr/master/docs/screenshots/demo.gif" alt="Muxarr Demo" width="800"/><br/>
  <em>A quick demo of a file being optimized (35.9% reduction)</em>
</p>

### Quick Start

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

## Features

- **Strip unwanted tracks** - remove audio (commentary, foreign dubs) and subtitles (SDH, foreign) without re-encoding, so quality is untouched. A typical 4GB file processes in about a minute depending on disk speed, saving up to 10% in file size.
- **Original language detection** - integrates with your *arr stack so foreign films and shows always keep the correct audio track
- **Automatic processing** - webhook support to process new imports as they arrive
- **Per-directory profiles** - different language and track rules for different collections (e.g. anime vs western media)
- **Language priority & track limits** - control track ordering per language, limit tracks per language (e.g. keep only the best English audio track), and choose between best quality or smallest size
- **Smart metadata fixes** - cleans up encoder tags and codec dumps from track names. Uses mkvpropedit for metadata-only changes (instant, no remux needed)
- **Safe by default** - validates the output file before replacing the original. If anything fails, the original is untouched.
- **Library overview** - full breakdown of codecs, resolutions, and languages across your library

Supports Matroska (`.mkv`, `.webm`) and MP4-family (`.mp4`, `.m4v`).

<details>
<summary>More screenshots</summary>
<br/>
<p align="center">
  <img src="https://raw.githubusercontent.com/KirovAir/muxarr/master/docs/screenshots/before.png" alt="Before" width="600"/><br/>
  ⬇️<br/>
  <img src="https://raw.githubusercontent.com/KirovAir/muxarr/master/docs/screenshots/after.png" alt="After" width="600"/><br/>
  <em>Before and after metadata cleanup</em>
</p>

<p align="center">
  <img src="https://raw.githubusercontent.com/KirovAir/muxarr/master/docs/screenshots/filedetails.png" alt="File Details" width="800"/><br/>
  <em>File details with track preview</em>
</p>

<p align="center">
  <img src="https://raw.githubusercontent.com/KirovAir/muxarr/master/docs/screenshots/dashboard.png" alt="Dashboard" width="800"/><br/>
  <em>Dashboard</em>
</p>

<p align="center">
  <img src="https://raw.githubusercontent.com/KirovAir/muxarr/master/docs/screenshots/settings.png" alt="Profile Settings" width="800"/><br/>
  <em>Profile settings</em>
</p>

<p align="center">
  <img src="https://raw.githubusercontent.com/KirovAir/muxarr/master/docs/screenshots/statistics.png" alt="Statistics" width="800"/><br/>
  <em>Statistics</em>
</p>
</details>

## Installation

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

### Volumes

| Path | Description |
|---|---|
| `/data` | Database and configuration |
| `/media` | Media files (use multiple `-v` mounts as needed) |

### Setup

1. Open `http://your-ip:8183` - the setup wizard will guide you through
2. Set a username and password (optional)
3. Connect Sonarr/Radarr for original language detection and webhook automation (optional)
4. Create a profile with your media directories and language rules
5. Scan your library, preview the changes, and queue files for processing

### Media Server Refresh

Muxarr can notify Jellyfin, Emby, and Plex after a successful conversion so your library can pick up the updated file.

For the most reliable refresh behavior, mount your media with the same absolute paths in both containers, for example `/mnt/media:/media` in Muxarr and your media server. Path mapping between different container paths is not supported.

### API

Muxarr exposes a stats API at `/api/stats` (authenticated via `X-Api-Key` header). Works with [Homepage](https://gethomepage.dev/widgets/services/customapi/) and other dashboards. See Settings > API for examples.

## Built With

- [.NET 10](https://dotnet.microsoft.com/) / Blazor
- [MKVToolNix](https://mkvtoolnix.download/) (mkvmerge, mkvpropedit)
- [FFmpeg](https://ffmpeg.org/) (ffmpeg, ffprobe)

## License

GPL-3.0 - see [LICENSE](LICENSE.md).

Muxarr is not affiliated with Sonarr, Radarr, or any other *arr projects.
