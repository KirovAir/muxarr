# Contributing to Muxarr

Thanks for wanting to help with Muxarr! This page explains how I run the project and what kind of changes tend to get merged, so you know before you put real work into something.

A quick word about response times. Some personal circumstances have slowed me down this year, so issues and PRs sometimes sit longer than I'd like. I'm still interested even when I'm slow to reply, I'm working on getting things sorted, and I hope to be a lot more present soon. A friendly ping after a few weeks is always welcome.

## What Muxarr is

Muxarr strips audio and subtitle tracks you don't want, and cleans up track metadata. It deliberately stays focused on those two jobs.

It does this without re-encoding. Matroska and WebM files go through mkvmerge, other containers go through ffmpeg with stream copy. The whole point is zero quality loss, so anything that touches the video or audio data itself (re-encoding, filters, cropping, transcoding) is out of scope, however cleanly it's implemented.

The other half of the idea is that it should just work. You set up a profile once and forget about it. So the UI stays simple and every setting has to earn its place: a new toggle, column or badge needs to solve a problem people have actually run into, not just show information that might be useful.

I'm the only one working on Muxarr, so everything I merge is something I'll still be maintaining years from now. If a PR introduces a new concept, a new setting, or an abstraction with a single caller, I'll most likely ask you to scope it down. If you disagree with any of this, open an issue and let's talk it through. I'd rather have that conversation in an issue than inside a PR review.

## Things I'd love help with

- Bug reports with a small sample file you can share, or a description of its container and tracks, that reproduces the problem
- Language data: wrong or missing names, families and variants in `iso_custom.json`
- Notification providers for services I don't use myself
- README fixes for anything you had to figure out the hard way. For the docs on muxarr.app, open an issue, that site lives in its own repo
- Testing pre-releases on setups I don't have, like Unraid, Synology or Windows

## Before you open a PR

For anything bigger than a small bug fix, **open an issue first**. Ten minutes of talking beats a thousand lines I can't merge, and once we agree on the shape, the PR itself is usually quick. Link the issue when you open the PR, and target `master`.

You can skip the issue for:

- Bug fixes with a clear reproduction, ideally with a regression test
- Typo fixes and comment cleanups
- Small dependency bumps, as long as CI stays green

Please start with an issue for:

- New features, settings or config fields
- New integrations (Sonarr/Radarr, notification providers, media servers)
- New API endpoints
- Schema changes
- Anything touching the conversion pipeline (`MediaConverterService`, `FFmpeg`, `MkvToolNix`)
- Refactors, renames, file moves
- UI additions or layout tweaks

## Where things go

Muxarr already has a few good homes for new functionality. Check whether one of them fits before building something next to them.

### Notification providers

If your feature is "send something when a conversion finishes or fails," it's a notification provider. That covers chat bots (Discord, Slack, Telegram), push services (Gotify, Ntfy, Pushover, Apprise), generic webhooks, and media server refreshes (Plex, Jellyfin, Emby).

Providers live in `Muxarr.Web/Services/Notifications/Providers/` and inherit `NotificationProvider<TSettings>`. Your settings class declares a few `[Field]`-decorated string or boolean properties, and the base class handles the modal, the binding and the `HttpClient`. `DiscordProvider` is a good minimal example; most providers are 30 to 40 lines.

Before adding a new `IntegrationType` or reaching for an `IMediaServerClient`-style interface, check whether a notification provider does the job. It usually does.

### Arr integrations

`IntegrationType` is for Sonarr and Radarr only, because they take part in the import pipeline: webhook ingestion, original-language lookup, media info sync. Something that doesn't take part in that pipeline belongs elsewhere, usually as a notification provider.

### The API

There's a small REST API under `/api`, authenticated with the `X-Api-Key` header. It's there for dashboards and for automating the queue, not as a second UI. An endpoint that mirrors a page one to one is probably not needed; one that lets a script do what the UI already does is fair game.

### Track filtering and profile logic

This lives in `Muxarr.Data/Extensions/MediaFileExtensions.cs`. The main functions are `GetAllowedTracks`, `ApplyProfileMutations` and `ApplyTrackMutations`. Anything in here may drop, rename, retag, flag or reorder tracks. It may never alter the audio or video data.

Tests for this area live in `TrackFilterTests.cs` and `TrackPriorityTests.cs`, and new filter logic needs new tests next to them.

## Code style

The `.editorconfig` covers formatting. A few things it can't express:

- Regular hyphens only. No em-dashes or en-dashes in code, comments or strings.
- Keep comments short and human. Explain why something isn't obvious, and skip comments that narrate what the code already shows.
- DB enums use `HasConversion<string>()` in their `EntityTypeConfiguration`. Int-backed enums break production data the moment someone reorders them. `IntegrationConfiguration` has the pattern.
- Entity logic goes in extension methods. Don't add a service class unless you actually need DI.
- Keep unrelated reformatting out of feature PRs. If your editor reformats on save, leave those changes out before pushing. A standalone formatting PR is fine.

When in doubt, match the code around you.

## Project layout

```
Muxarr.Core/    FFmpeg and MkvToolNix wrappers, Arr API client,
                language metadata, config types
Muxarr.Data/    EF Core entities, DbContext, migrations, SQLite interceptor
Muxarr.Web/     Blazor UI, controllers, services, auth, notification providers
Muxarr.Tests/   MSTest suite and .mkv fixtures in Fixtures/
```

## Building and running

You need the .NET 10 SDK (`global.json` sets 10.0.100 as the baseline), plus the binaries Muxarr shells out to:

- **mkvtoolnix** (mkvmerge, mkvpropedit)
- **ffmpeg 8.1 or newer** (ffmpeg and ffprobe). Older versions break MP4 `udta.name` round-tripping, so Debian's apt 5.1 won't do. The Docker image pulls a BtbN static build; see `Muxarr.Web/Dockerfile`.

On macOS: `brew install mkvtoolnix ffmpeg`.
On Debian/Ubuntu: `apt install mkvtoolnix`, plus a BtbN static ffmpeg build.
On Windows: `choco install mkvtoolnix ffmpeg`, and check that `ffmpeg -version` reports 8.1 or newer.

Then:

```bash
dotnet restore
dotnet build
dotnet run --project Muxarr.Web
```

It comes up on port 8183.

## Testing

```bash
dotnet test
```

Tests are MSTest and shell out to the real `ffmpeg`, `ffprobe`, `mkvmerge` and `mkvpropedit` against the fixtures in `Muxarr.Tests/Fixtures/`. CI runs them on Linux (amd64 and arm64), macOS and Windows. Please make sure the suite is green before asking for review.

Bug fixes should come with a regression test, and new pipeline logic needs tests around it. That area is gnarly enough that "works on my machine" has burned me before. Keep tests proportional though: a one-line fix doesn't need a hundred lines of them.

## Database changes

Schema changes go through EF Core migrations. The CLI isn't part of the SDK, so install it once if you haven't:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add DescriptiveName --project Muxarr.Data --startup-project Muxarr.Web
```

Before opening the PR:

- Commit both the migration and the updated `AppDbContextModelSnapshot.cs`.
- Make sure `dotnet ef database update` runs cleanly on a fresh database.
- Boot the app against an existing pre-migration database. The first start backs it up to `muxarr.db.bak`; check that still happens.
- New enum columns use `HasConversion<string>()`.

Migrations must preserve existing data when they drop columns or rename tables.

## Pull request hygiene

- One concern per PR. Don't mix a feature with a refactor and a bug fix.
- Give the PR a clear title and body, link the issue, and say why the change is needed more than what it does.
- Screenshots for UI changes, before and after if you can.

## One last thing

I will occasionally decline a contribution that is genuinely well written, simply because the idea isn't the right fit for what this tool is for. If that happens to you, it really isn't personal, and I still appreciate the work and the offer to help.

Thanks for reading all of this, and for caring about a small tool enough to want to improve it. That means a lot.
