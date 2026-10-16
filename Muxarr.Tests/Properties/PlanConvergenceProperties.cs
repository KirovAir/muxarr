using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests.Properties;

/// <summary>
/// Property tests over generated files and profiles, against the real planner.
/// A conversion is simulated in memory: the plan is applied to the snapshot the
/// way the writers and the scanner would see it. Planning the result again must
/// yield Skip, and planning twice must give the same plan.
/// </summary>
[TestClass]
public class PlanConvergenceProperties
{
    // MUXARR_PROP_CASES for a longer run, MUXARR_PROP_REPLAY="seed,gamma" to
    // replay the run FsCheck printed after a failure.
    internal static FsCheck.Config Config
    {
        get
        {
            var cases = int.TryParse(Environment.GetEnvironmentVariable("MUXARR_PROP_CASES"), out var n) ? n : 400;
            var config = FsCheck.Config.QuickThrowOnFailure.WithMaxTest(cases);
            var replay = Environment.GetEnvironmentVariable("MUXARR_PROP_REPLAY")?.Split(',');
            if (replay is { Length: 2 } && ulong.TryParse(replay[0], out var seed) && ulong.TryParse(replay[1], out var gamma))
            {
                config = config.WithReplay(new Replay(new Rnd(seed, gamma), Microsoft.FSharp.Core.FSharpOption<int>.None));
            }

            return config;
        }
    }

    [TestMethod]
    public void Plan_Is_Deterministic()
    {
        Check.One(Config, Prop.ForAll(Arb.From(CaseGen()), c =>
        {
            var (file, profile) = c.Materialize();
            var a = Json(file.BuildTargetFromProfile(profile));
            var b = Json(file.BuildTargetFromProfile(profile));
            Assert.AreEqual(a, b, c.Describe());
        }));
    }

    [TestMethod]
    public void Applying_The_Plan_Converges_To_Skip()
    {
        Check.One(Config, Prop.ForAll(Arb.From(CaseGen()), c =>
        {
            var (file, profile) = c.Materialize();
            var plan = file.BuildTargetFromProfile(profile);
            var first = ConversionPlanner.Plan(file.Snapshot, plan);
            if (first.Strategy == ConversionPlanner.ConversionStrategy.Skip)
            {
                return;
            }

            var after = Simulate(file, plan, first);
            var secondPlan = after.BuildTargetFromProfile(profile);
            var second = ConversionPlanner.Plan(after.Snapshot, secondPlan);
            if (second.Strategy == ConversionPlanner.ConversionStrategy.Skip)
            {
                return;
            }

            // The undetermined rule only applies once a type is down to a single
            // track, so a removal can make it fire on the next pass. That is a
            // second conversion by design, not a loop; anything else is.
            var detail = $"{c.Describe()}\n\nfirst: {first.Strategy}\n{Json(first.Delta)}\n\nafter:\n{Describe(after.Snapshot)}\n\nsecond: {second.Strategy}\n{Json(second.Delta)}";
            Assert.IsTrue(UndeterminedRuleJustBecameApplicable(file, after, profile), "did not converge in one conversion\n" + detail);

            var third = Simulate(after, secondPlan, second);
            var thirdResult = ConversionPlanner.Plan(third.Snapshot, third.BuildTargetFromProfile(profile));
            Assert.AreEqual(ConversionPlanner.ConversionStrategy.Skip, thirdResult.Strategy,
                $"did not converge in two conversions\n{detail}\n\nthird:\n{Describe(third.Snapshot)}\n{Json(thirdResult.Delta)}");
        }));
    }

    internal static bool UndeterminedRuleJustBecameApplicable(MediaFile before, MediaFile after, Profile profile)
    {
        foreach (var type in new[] { MediaTrackType.Audio, MediaTrackType.Subtitles })
        {
            var settings = type == MediaTrackType.Audio ? profile.AudioSettings : profile.SubtitleSettings;
            var remaining = after.Snapshot.Tracks.Where(t => t.Type == type).ToList();
            if (settings.AssumeUndeterminedIsOriginal
                && before.Snapshot.Tracks.Count(t => t.Type == type) > 1
                && remaining.Count == 1
                && remaining[0].ShouldResolveUndetermined(settings, 1, after.OriginalLanguage))
            {
                return true;
            }
        }

        return false;
    }

    // --- simulated conversion ---

    // What the file looks like after the writer ran and the scanner read it back.
    internal static MediaFile Simulate(MediaFile file, ConversionPlan plan, ConversionPlanner.PlanResult result,
        IEnumerable<SubtitleFile>? sidecars = null)
    {
        var source = file.Snapshot;
        var family = source.ContainerType.ToContainerFamily();
        var delta = result.Delta;
        var remux = result.Strategy == ConversionPlanner.ConversionStrategy.Remux;
        var deltaByIndex = delta.Tracks.ToDictionary(t => t.Index);
        var sourceByIndex = source.Tracks.ToDictionary(t => t.Index);
        foreach (var sidecar in sidecars ?? [])
        {
            sourceByIndex[sidecar.Index] = sidecar.ToSnapshot();
        }

        var tracks = new List<TrackSnapshot>();
        var position = 0;
        foreach (var planned in plan.Tracks)
        {
            var track = sourceByIndex[planned.Index].ToSnapshot();
            var d = deltaByIndex[planned.Index];
            if (d.Name != null)
            {
                track.Name = d.Name == "" ? null : d.Name;
            }

            if (d.LanguageCode != null)
            {
                track.LanguageCode = d.LanguageCode;
                track.LanguageName = IsoLanguage.Find(d.LanguageCode).Name;
            }

            track.IsDefault = d.IsDefault ?? track.IsDefault;
            track.IsForced = d.IsForced ?? track.IsForced;
            track.IsHearingImpaired = d.IsHearingImpaired ?? track.IsHearingImpaired;
            track.IsVisualImpaired = d.IsVisualImpaired ?? track.IsVisualImpaired;
            track.IsCommentary = d.IsCommentary ?? track.IsCommentary;
            track.IsOriginal = d.IsOriginal ?? track.IsOriginal;
            track.IsDub = d.IsDub ?? track.IsDub;

            AsScanned(track, family);
            track.Index = remux ? position++ : planned.Index;
            tracks.Add(track);
        }

        var snapshot = new MediaSnapshot
        {
            CapturedAt = DateTime.UtcNow,
            ContainerType = source.ContainerType,
            Title = delta.Title == null ? source.Title : delta.Title == "" ? null : delta.Title,
            HasChapters = delta.HasChapters ?? source.HasChapters,
            HasAttachments = delta.HasAttachments ?? source.HasAttachments,
            // RunFFmpegRemuxAsync restores the source's faststart when the delta has no opinion.
            HasFaststart = family == ContainerFamily.Mp4 && (delta.Faststart ?? source.HasFaststart),
            DurationMs = source.DurationMs,
            TrackCount = tracks.Count,
            Tracks = tracks
        };

        return new MediaFile { Path = file.Path, OriginalLanguage = file.OriginalLanguage, Snapshot = snapshot };
    }

    // The scanner reads flags off the name as a second source, Matroska has no
    // dub flag, and the mov muxer drops the original flag. Any state the planner
    // sees has been through this.
    internal static void AsScanned(TrackSnapshot track, ContainerFamily family)
    {
        if (track.Type == MediaTrackType.Video)
        {
            return;
        }

        track.IsForced |= TrackNameFlags.ContainsForced(track.Name);
        track.IsHearingImpaired |= TrackNameFlags.ContainsHearingImpaired(track.Name);
        track.IsVisualImpaired |= TrackNameFlags.ContainsVisualImpaired(track.Name);
        track.IsCommentary |= TrackNameFlags.ContainsCommentary(track.Name);
        if (family == ContainerFamily.Matroska)
        {
            track.IsDub = TrackNameFlags.ContainsDub(track.Name);
        }
        else
        {
            track.IsDub |= TrackNameFlags.ContainsDub(track.Name);
            track.IsOriginal = false;
        }

        track.RefineLanguageFromName();
    }

    // --- generators ---

    private static readonly string[] Languages = ["English", "Dutch", "French", "Japanese", "Undetermined"];
    private static readonly string[] AudioNames = [null!, "English 5.1", "Commentary", "French Dub", "AD", "Surround"];
    private static readonly string[] SubNames = [null!, "English", "Forced", "SDH", "Full", "VFQ", "Signs"];

    private static Gen<TrackSnapshot> AudioGen(int index)
    {
        return from lang in Gen.Elements(Languages)
            from codec in Gen.Elements(nameof(AudioCodec.Aac), nameof(AudioCodec.Ac3), nameof(AudioCodec.TrueHd))
            from channels in Gen.Elements(2, 6, 8)
            from commentary in Bool()
            from hi in Bool()
            from vi in Bool()
            from def in Bool()
            from dub in Bool()
            from original in Bool()
            from name in Gen.Elements(AudioNames)
            select TestData.Audio(index, lang, codec, channels, commentary, hi, def, dub, original, name, vi: vi);
    }

    private static Gen<TrackSnapshot> SubGen(int index, ContainerFamily family)
    {
        var codecs = family == ContainerFamily.Matroska
            ? new[] { nameof(SubtitleCodec.Srt), nameof(SubtitleCodec.Ass), nameof(SubtitleCodec.Pgs) }
            : [nameof(SubtitleCodec.MovText)];
        return from lang in Gen.Elements(Languages)
            from codec in Gen.Elements(codecs)
            from forced in Bool()
            from hi in Bool()
            from commentary in Bool()
            from def in Bool()
            from name in Gen.Elements(SubNames)
            select Flag(TestData.Sub(index, lang, codec, forced, hi, commentary, trackName: name), def);
    }

    private static Gen<List<T>> Sequence<T>(IEnumerable<Gen<T>> gens)
    {
        var result = Gen.Constant(new List<T>());
        foreach (var gen in gens)
        {
            var current = result;
            result = from list in current from item in gen select list.Append(item).ToList();
        }

        return result;
    }

    private static Gen<bool> Bool()
    {
        return Gen.Elements(true, false);
    }

    private static TrackSnapshot Flag(TrackSnapshot t, bool isDefault)
    {
        t.IsDefault = isDefault;
        return t;
    }

    private static Gen<string> PreferenceGen()
    {
        return from lang in Gen.Elements("English", "Dutch", "French", "Japanese", "Undetermined",
                IsoLanguage.OriginalLanguageName)
            from fallback in Bool()
            from max in Gen.Elements(null, (int?)1, 2)
            select JsonSerializer.Serialize(new
            {
                Language = lang == IsoLanguage.OriginalLanguageName ? IsoLanguage.OriginalLanguage : IsoLanguage.Find(lang),
                IsFallback = fallback,
                MaxTracks = max
            });
    }

    // Settings as JSON so the same generator works on branches that changed the
    // removal switches into policies (they still read the old names).
    private static Gen<string> SettingsGen(bool audio)
    {
        return from enabled in Gen.Frequency((3, Gen.Constant(true)), (1, Gen.Constant(false)))
            from count in Gen.Choose(0, 3)
            from prefs in Gen.ArrayOf(PreferenceGen(), count)
            from commentary in Bool()
            from impaired in Bool()
            from assumeUnd in Bool()
            from def in Gen.Choose(0, 2)
            from reorder in Gen.Choose(0, 2)
            from standardize in Bool()
            from template in Gen.Elements("{language} {channels}", "{language}", "{code}")
            from exclude in Bool()
            select "{" +
                   $"\"Enabled\":{(enabled ? "true" : "false")}," +
                   $"\"AllowedLanguages\":[{string.Join(",", prefs)}]," +
                   $"\"RemoveCommentary\":{(commentary ? "true" : "false")}," +
                   $"\"RemoveImpaired\":{(impaired ? "true" : "false")}," +
                   $"\"AssumeUndeterminedIsOriginal\":{(assumeUnd ? "true" : "false")}," +
                   $"\"DefaultStrategy\":{def}," +
                   $"\"ReorderStrategy\":{reorder}," +
                   $"\"StandardizeTrackNames\":{(standardize ? "true" : "false")}," +
                   $"\"TrackNameTemplate\":\"{template}\"," +
                   $"\"ExcludeCodecs\":{(exclude ? "true" : "false")}," +
                   (audio
                       ? $"\"ExcludedAudioCodecs\":[{(int)AudioCodec.TrueHd}]"
                       : $"\"ExcludedCodecs\":[{(int)SubtitleCodec.Pgs}]") +
                   "}";
    }

    internal static Gen<Case> CaseGen()
    {
        return from container in Gen.Elements("Matroska", "MP4/QuickTime")
            let family = container.ToContainerFamily()
            from original in Gen.Elements(null, "English", "French", "Japanese")
            from audioCount in Gen.Choose(0, 3)
            from subCount in Gen.Choose(0, 4)
            from audios in Sequence(Enumerable.Range(1, audioCount).Select(AudioGen))
            from subs in Sequence(Enumerable.Range(1 + audioCount, subCount).Select(i => SubGen(i, family)))
            from title in Gen.Elements(null, "", "Big Buck Bunny")
            from chapters in Bool()
            from faststart in Bool()
            from audioSettings in SettingsGen(true)
            from subSettings in SettingsGen(false)
            from clearVideo in Bool()
            from clearTitle in Bool()
            from removeChapters in Bool()
            from trim in Bool()
            select new Case(container, original, audios.Concat(subs).ToList(), title, chapters, faststart,
                audioSettings, subSettings, clearVideo, clearTitle, removeChapters, trim);
    }

    internal sealed record Case(
        string Container,
        string? OriginalLanguage,
        List<TrackSnapshot> Tracks,
        string? Title,
        bool HasChapters,
        bool HasFaststart,
        string AudioSettings,
        string SubtitleSettings,
        bool ClearVideoNames,
        bool ClearFileTitle,
        bool RemoveChapters,
        bool Trim)
    {
        public (MediaFile, Profile) Materialize()
        {
            var tracks = new List<TrackSnapshot> { TestData.Video(0, "Video") };
            tracks.AddRange(Tracks.Select(t => t.ToSnapshot()));
            var file = TestData.MakeFile(OriginalLanguage, tracks.ToArray());
            file.Path = "/lib/Movie/Movie." + (Container == "Matroska" ? "mkv" : "mp4");
            file.Snapshot.ContainerType = Container;
            file.Snapshot.Title = Title;
            file.Snapshot.HasChapters = HasChapters;
            file.Snapshot.HasFaststart = Container != "Matroska" && HasFaststart;
            file.Snapshot.DurationMs = 60_000;
            foreach (var t in file.Snapshot.Tracks)
            {
                AsScanned(t, Container.ToContainerFamily());
            }

            var profile = new Profile
            {
                Name = "generated",
                AudioSettings = JsonSerializer.Deserialize<TrackSettings>(AudioSettings)!,
                SubtitleSettings = JsonSerializer.Deserialize<TrackSettings>(SubtitleSettings)!,
                ClearVideoTrackNames = ClearVideoNames,
                ClearFileTitle = ClearFileTitle,
                RemoveChapters = RemoveChapters,
                TrimToVideoLength = Trim
            };
            return (file, profile);
        }

        public string Describe()
        {
            var (file, _) = Materialize();
            return $"container={Container} original={OriginalLanguage} title={Title} chapters={HasChapters} faststart={HasFaststart}\n" +
                   $"clearVideo={ClearVideoNames} clearTitle={ClearFileTitle} removeChapters={RemoveChapters} trim={Trim}\n" +
                   $"audio={AudioSettings}\nsubs={SubtitleSettings}\n{PlanConvergenceProperties.Describe(file.Snapshot)}";
        }
    }

    internal static string Describe(MediaSnapshot snapshot)
    {
        return string.Join("\n", snapshot.Tracks.Select(t =>
            $"  #{t.Index} {t.Type} {t.LanguageName}({t.LanguageCode}) {t.Codec} ch={t.AudioChannels} name='{t.Name}'" +
            $"{(t.IsDefault ? " default" : "")}{(t.IsForced ? " forced" : "")}{(t.IsHearingImpaired ? " sdh" : "")}" +
            $"{(t.IsVisualImpaired ? " ad" : "")}{(t.IsCommentary ? " commentary" : "")}{(t.IsOriginal ? " original" : "")}{(t.IsDub ? " dub" : "")}"))
               + $"\n  title='{snapshot.Title}' chapters={snapshot.HasChapters} faststart={snapshot.HasFaststart}";
    }

    private static string Json(object o)
    {
        return JsonSerializer.Serialize(o, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }
}
