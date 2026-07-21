ARG FFMPEG_VERSION=8.1

# ffmpeg static build from BtbN (linked from ffmpeg.org). Debian's
# apt ffmpeg is 5.1 - too old to round-trip MP4 udta.name titles.
FROM debian:12-slim AS ffmpeg-dl
ARG FFMPEG_VERSION
ARG TARGETARCH
RUN apt-get update && apt-get install -y --no-install-recommends \
      curl ca-certificates xz-utils \
    && rm -rf /var/lib/apt/lists/*
RUN set -eux; \
    case "${TARGETARCH}" in \
        amd64) btbn_arch=linux64 ;; \
        arm64) btbn_arch=linuxarm64 ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    url="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n${FFMPEG_VERSION}-latest-${btbn_arch}-gpl-${FFMPEG_VERSION}.tar.xz"; \
    curl -fsSL "${url}" -o /tmp/ffmpeg.tar.xz; \
    mkdir -p /out; \
    tar -xJf /tmp/ffmpeg.tar.xz -C /out --strip-components=2 --wildcards '*/bin/ffmpeg' '*/bin/ffprobe'; \
    /out/ffmpeg -version

FROM mcr.microsoft.com/dotnet/aspnet:10.0

RUN apt-get update && apt-get install -y --no-install-recommends \
    gosu \
    curl \
    mkvtoolnix \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd -g 888 appgroup \
    && useradd -u 888 -g 888 -d /config -s /bin/false appuser \
    && usermod -G users appuser

COPY --from=ffmpeg-dl /out/ffmpeg /usr/local/bin/ffmpeg
COPY --from=ffmpeg-dl /out/ffprobe /usr/local/bin/ffprobe

WORKDIR /app

COPY publish/ ./
COPY entrypoint.sh ./entrypoint.sh
RUN chmod +x ./entrypoint.sh

VOLUME ["/config"]

ENV ASPNETCORE_HTTP_PORTS=8183
EXPOSE 8183

ENTRYPOINT ["./entrypoint.sh", "dotnet", "Muxarr.Web.dll"]
