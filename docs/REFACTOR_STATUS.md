# Conversion Refactor Status

Tracking doc for the `feature/engine-v3` refactor. Lives next to `docs/CONVERSION.md` which documents the runtime architecture. This file is about what changed, what works, what still bothers me, and what to do next if I pick this up again.

## Goal

Unify the conversion pipeline around a single desired-state type, make converters dumb (one signature, no container branching), and resolve container quirks in exactly one place. Add mkvpropedit support for the MetadataEdit strategy.

## Shipped

### New types (Muxarr.Core.Models)

- `TargetTrack`: nullable-fielded desired state per track. `null` = inherit source, non-null = set, `""` on `Name` = explicit clear. `NameLocked` bit governs whether the planner can rewrite the title.
- `TargetSnapshot`: container-level desired state. Holds `List<TargetTrack>` plus `HasChapters?`, `HasAttachments?`, `Faststart?`.
- `ConversionPlan` record: `TargetSnapshot Delta, long SourceDurationMs`. Converter-facing payload.

### `TargetDiff` (Muxarr.Data.Extensions)

- `Delta(MediaSnapshot source, TargetSnapshot desired) -> TargetSnapshot` and per-track overload.
- `HasChanges(TargetSnapshot)` / `HasChanges(TargetTrack)`.
- Single `DiffString` helper (was duplicated as `DiffName` + `DiffString`, collapsed during review).

### `ConversionPlanner` (Muxarr.Web.Services)

- `Plan(MediaFile, MediaSnapshot, TargetSnapshot) -> PlanResult(Strategy, ConversionPlan)`.
- `ResolveContainerQuirks` is the only place Matroska's lack of FlagDub is handled. Rewrites titles (if `!NameLocked`) and nulls `IsDub` for every track type. After this call, `IsDub` is always null on Matroska targets.
- Strategy selection: structural change -> Remux. No changes -> Skip. Matroska + metadata only -> MetadataEdit. Anything else -> Remux.

### Unified converters

All three take the same signature: `(string input, string output, ConversionPlan plan, Action<string,int>? onProgress, TimeSpan? timeout)`.

- `MkvMerge.Remux` writes via `mkvmerge -o`. Reads `plan.Delta.HasChapters/HasAttachments` to emit `--no-chapters` / `--no-attachments`.
- `FFmpeg.Remux` writes stream-copy via `ffmpeg -c copy`. Reads `plan.Delta.Faststart` for `+faststart` movflag, `plan.SourceDurationMs` for progress.
- `MkvPropEdit.Apply` edits in place via `mkvpropedit`. Does not handle `IsDub` (Matroska has no FlagDub; planner strips it). Has a guard: if no `--edit` args get emitted, returns success without invoking the tool.

### Builders (Muxarr.Data.Extensions.MediaFileExtensions)

- `BuildTargetFromProfile(file, profile)`: runs existing profile mutations plus `ApplyTrackMutations` which now auto-sets `IsOriginal` on audio based on `file.OriginalLanguage`. Sets `NameLocked=true` when a title came from `TrackNameOverrides`. Sets `Name=""` on video when `ClearVideoTrackNames`.
- `BuildTargetFromCustom(file, userEditedTracks)`: no profile mutations. Only ISO-normalizes language codes. Every track is `NameLocked=true`.
- `ToTargetSnapshotFromSource(file)`: pass-through target when no profile applies.
- `ToTargetTrack(TrackSnapshot, bool nameLocked)`: conversion helper.
- Old `BuildTargetSnapshot(profile)` returning `MediaSnapshot` kept for UI preview.

### Dispatcher (`MediaConverterService.HandleConversion`)

- Rescans file.
- Profile path: rebuilds `TargetSnapshot` from fresh source.
- Custom path: validates target tracks exist on rescan; fails fast with a clear log if not.
- Calls `ConversionPlanner.Plan`.
- Switches on strategy: Skip / MetadataEdit -> `MkvPropEdit.Apply` / Remux -> `MkvMerge.Remux` (Matroska) or `FFmpeg.Remux` (MP4).
- Post-edit rescan in the propedit path falls through to full remux if changes did not stick.

### Persistence

- `MediaConversion.TargetSnapshot` is now the `TargetSnapshot` type (serialized as JSON).
- `SnapshotBefore` / `SnapshotAfter` stay as `MediaSnapshot` (observed state).
- EF migration `20260413153109_TargetSnapshotRefactor` drops non-terminal `MediaConversion` rows on upgrade so in-flight work re-queues cleanly. Terminal rows (Completed / Failed / Cancelled) stay.

### Bug fixes landed alongside

- Custom conversions no longer run profile mutations. `ApplyTrackMutations` was being called with `standardizeNames: false`, which still executed `CorrectFlagsFromTrackName` (flipped user-unchecked flags back on via title detection) and `ShouldResolveUndetermined` (overwrote `und` languages). Custom path now skips both.
- Scanner reads `disposition.dub` only for non-Matroska containers. FFmpeg's matroska demuxer infers `disposition.dub=1` from `FlagOriginal=0`, which made every not-original track look like a dub. For MKV the title is authoritative; for MP4 the native disposition is.
- UI's `ToggleDub` normalizes strip-to-empty as `""` not `null` so the custom path carries explicit-clear intent through the delta.
- Planner strips `IsDub` for every Matroska track type, not just audio. The earlier audio-only check let subtitle-IsDub opinions leak into deltas that mkvpropedit could not express, producing `Error: Nothing to do`.
- `MkvPropEdit.Apply` has a defensive `editCount == 0` guard. Any future plan/apply drift becomes a silent skip with a log line instead of a hard failure.

### Tests

459 passing, 1 skipped (`GenerateIso639Data`). See `Muxarr.Tests/Integration/ComplexConversionTests.cs` for the wide-coverage integration suite and `Muxarr.Tests/ConversionPipelineTests.cs` for the unit-level IsOriginal auto-set tests.

## Open concerns

### ~~1. Planner mutation + EF persistence~~ FIXED

Moved `ResolveContainerQuirks` out of `ConversionPlanner` into a new
`TargetResolver.ResolveForContainer` in `Muxarr.Data.Extensions`. The three
builders (`BuildTargetFromProfile`, `BuildTargetFromCustom`,
`ToTargetSnapshotFromSource`) each call it as their last step, so the
`TargetSnapshot` they return is already resolved for the source container.
The planner is now a pure function: inputs are read-only, delta is the sole
output. The stored `TargetSnapshot` is correct from queue time, not
post-hoc. Preview and pipeline both see the same resolved state.

### 2. UI preview + pipeline type bridge (`MergeForDisplay` / `ToDisplay`)

`BuildTargetSnapshot(profile)` still returns `MediaSnapshot` for the UI.
Internally it calls `BuildTargetFromProfile` and then merges back via
`TargetSnapshot.MergeForDisplay(file)`, which delegates per-track work to
`TargetTrack.ToDisplay(source)`. The helper is now split by responsibility:
the outer method is six lines, the per-track merge carries the ~15 field
assignments that bridge the two snapshot shapes.

The remaining smell is structural: two snapshot types (`MediaSnapshot` with
full-state fields and `TargetSnapshot` with nullable desired-state fields)
need glue everywhere they meet. `ToDisplay` is one of those points. Another
is `TrackSnapshot.ToTargetTrack` in the builders. Another is
`TargetDiff.Delta`. None of them are individually large after the split,
but the aggregate is real complexity.

Clean resolution is picking one type:
- kill `MediaSnapshot` and have the UI consume `TargetSnapshot + MediaFile` directly; or
- kill the nullable semantics and collapse into one type.

Either removes `ToDisplay` entirely. Neither is small. Leave for a future
pass unless the two-type split keeps causing friction.

### 3. `MediaTrackType` namespace trick

The enum's file lives under `Muxarr.Core/Models/` but declares `namespace Muxarr.Data.Entities`. This lets Core reference it without introducing a circular project dependency. It works, but it is weird enough to slow down anyone reading the project tree. Clean fix is renaming the namespace across every caller.

### 4. `ConversionPlan.SourceDurationMs` is ffmpeg-only

Only `FFmpeg.Remux` reads it. Lives on the tool-agnostic record for signature uniformity. Small smell.

### 5. `NameIsFromOverride` duplicates `TrackSettings.ResolveTemplate`

Both iterate `TrackFlagExtensions.All` looking for the first matching override. ~5 lines of duplicated logic. Could add `TryGetMatchingOverride(track, out template)` on `TrackSettings` and share.

### 6. Dispatcher mutation in `RunFFmpegRemuxAsync`

`plan.Delta.Faststart ??= mediaFile.HasFaststart` is another planner-external mutation of the plan. Same category smell as concern #1, smaller impact because FFmpeg.Remux runs later in the flow.

### 7. Custom conversion modal has no Original toggle

Buttons for Default / HI / Forced / Commentary / Dub. Profile path now auto-sets `IsOriginal`, but custom path gives the user no way to override it from the modal. Easy to close: add a button mirroring the others.

## Cleanups applied during review

- Collapsed `DiffName` and `DiffString` in `TargetDiff` (identical bodies).
- Simplified `ToTargetSnapshotFromSource` to build `TargetTrack` directly instead of constructing a `TrackSnapshot` and immediately converting.

## If I pick this up in a new session

1. Fix concern #1 (planner mutation). Deep-clone `desired` on entry to `Plan`. Move the `??= HasFaststart` mutation in the FFmpeg dispatcher next to the planner or fold it into the profile builder so the plan is immutable after `Plan` returns.
2. Decide on concern #7 (Original toggle in modal). If yes, add button + tooltip matching the others. No new planner logic needed.
3. Optional: concern #2 (unify UI preview and pipeline target). This is a bigger change and only worth it if the two-paths split starts causing drift. Not urgent.
4. Optional: concern #3 (MediaTrackType namespace). Pure cosmetic. Only worth the churn if a bigger file reorg is happening anyway.

Concerns #4, #5, #6 are small smells. Address only if something in that area gets touched for another reason.

## Reference docs

- `docs/CONVERSION.md`: runtime architecture (types, planner, converters, dispatcher). The how-it-works doc.
- `docs/REFACTOR_STATUS.md`: this file. The what-changed and what-is-left doc.
