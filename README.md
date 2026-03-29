<p align="center">
  <img src="Logo/logo.png" alt="Muxarr" width="120"/><br/>
  <a href="https://github.com/KirovAir/muxarr/actions/workflows/build-and-deploy.yml"><img src="https://github.com/KirovAir/muxarr/actions/workflows/build-and-deploy.yml/badge.svg" alt="Build and Deploy"/></a>
  <a href="https://github.com/KirovAir/muxarr/pkgs/container/muxarr"><img src="https://img.shields.io/badge/ghcr.io-kirovair%2Fmuxarr-blue?logo=docker" alt="Docker Image"/></a>
  <a href="https://www.gnu.org/licenses/gpl-3.0"><img src="https://img.shields.io/badge/License-GPLv3-blue.svg" alt="License: GPL v3"/></a>
</p>

# Muxarr

Muxarr strips unwanted audio and subtitle tracks from your media files using MKVMerge. It integrates with Sonarr and Radarr to detect original languages and can automatically process new files via webhooks.

> 🐉 Here be dragons. This project is still young, things may break. PRs and issues are welcome!

## What it does

- Remove unwanted audio tracks (commentary, foreign languages) and subtitles (SDH, foreign)
- Keep original language tracks using metadata from Sonarr/Radarr
- Profiles with per-directory rules for language filtering
- Webhook support for automatic processing of new Sonarr/Radarr imports
- Conversion queue with pause/resume/cancel

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

## Built With

- [.NET 9](https://dotnet.microsoft.com/) / Blazor
- [MKVToolNix](https://mkvtoolnix.download/) (mkvmerge)
- [FFmpeg](https://ffmpeg.org/)

## License

GPL-3.0 - see [LICENSE](LICENSE.md).

Muxarr is not affiliated with Sonarr, Radarr, or any other *arr projects.
