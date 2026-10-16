using FsCheck;
using FsCheck.Fluent;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests.Properties;

/// <summary>
/// docs/design-subtitle-files.html rules 2 to 7 against the real planner, with
/// the directory simulated. A conversion applies the plan to the container the
/// way the writers would and moves the files the way the converter does.
/// </summary>
[TestClass]
public class SubtitleLocationModelProperties
{
    private const string MediaPath = "/lib/Movie/Movie.mkv";

    internal sealed record World(MediaFile File, Profile Profile, List<SubtitleFile> Files);

    internal sealed record Moves(ConversionPlan Plan, List<SubtitleFile> MuxIn, List<SubtitleFile> Extracted, List<string> Deleted);

    [TestMethod]
    public void Plan_Is_Deterministic()
    {
        Check.One(PlanConvergenceProperties.Config, Prop.ForAll(Arb.From(WorldGen()), w =>
        {
            Assert.AreEqual(Describe(Plan(w)), Describe(Plan(w)), DescribeWorld(w));
        }));
    }

    [TestMethod]
    public void Where_They_Are_Never_Moves_Or_Deletes()
    {
        Check.One(PlanConvergenceProperties.Config, Prop.ForAll(Arb.From(WorldGen()), w =>
        {
            w.Profile.SubtitleSettings.Location = SubtitleLocation.WhereTheyAre;
            var moves = Plan(w);
            Assert.AreEqual(0, moves.MuxIn.Count + moves.Extracted.Count + moves.Deleted.Count, DescribeWorld(w));

            var plain = w.File.Snapshot.Tracks.GetSubtitleTracks()
                .GetAllowedTracks(w.Profile.SubtitleSettings, w.File.OriginalLanguage).Select(t => t.Index).ToList();
            CollectionAssert.AreEqual(plain, moves.Plan.Tracks.Where(t => t.Type == MediaTrackType.Subtitles).Select(t => t.Index).ToList(), DescribeWorld(w));
        }));
    }

    [TestMethod]
    public void One_Conversion_Converges_And_Loses_Nothing()
    {
        Check.One(PlanConvergenceProperties.Config, Prop.ForAll(Arb.From(WorldGen()), w =>
        {
            var moves = Plan(w);
            var after = Apply(w, moves);
            var again = Plan(after);
            var detail = $"{DescribeWorld(w)}\n\nmoves:\n{Describe(moves)}\n\nafter:\n{DescribeWorld(after)}\n\nsecond moves:\n{Describe(again)}";
            var deleteOn = w.Profile.SubtitleSettings.DeleteMovedSubtitleFiles;

            if (!deleteOn)
            {
                Assert.AreEqual(0, moves.Deleted.Count, "deleted with the switch off\n" + detail);
                Assert.IsTrue(w.Files.All(f => after.Files.Any(a => a.Suffix == f.Suffix)), "a file vanished with the switch off\n" + detail);
            }
            else
            {
                var moved = moves.MuxIn.Select(m => m.FileName(MediaPath))
                    .Concat(moves.Plan.ExtractedSubtitles.Select(e => e.FileName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Assert.IsTrue(moves.Deleted.All(moved.Contains), "deleted a file that was not just moved or replaced\n" + detail);
            }

            foreach (var kept in moves.Plan.Tracks.Where(t => t.Type == MediaTrackType.Subtitles))
            {
                Assert.IsTrue(after.File.Snapshot.Tracks.Any(t => Identity(t) == Identity(kept, w)),
                    $"kept subtitle #{kept.Index} is gone: wanted {Identity(kept, w)}, have [{string.Join(", ", after.File.Snapshot.Tracks.Select(Identity))}]\n{detail}");
            }

            foreach (var extracted in moves.Extracted)
            {
                Assert.IsTrue(after.Files.Any(f => f.Suffix == extracted.Suffix), $"extracted {extracted.Suffix} is gone\n{detail}");
            }

            if (again.MuxIn.Count + again.Extracted.Count + again.Deleted.Count > 0)
            {
                Assert.IsTrue(PlanConvergenceProperties.UndeterminedRuleJustBecameApplicable(w.File, after.File, w.Profile),
                    "second conversion still moves\n" + detail);
                var third = Plan(Apply(after, again));
                Assert.AreEqual(0, third.MuxIn.Count + third.Extracted.Count + third.Deleted.Count, "third conversion still moves\n" + detail);
            }
        }));
    }

    internal static Moves Plan(World w)
    {
        w.File.SubtitleFiles = w.Files;
        var plan = w.File.BuildTargetFromProfile(w.Profile);
        var byPath = w.File.Sidecars().ToDictionary(f => f.FullPath(MediaPath));
        var muxIn = plan.Tracks.Where(t => t.SourcePath != null).Select(t => byPath[t.SourcePath!]).ToList();
        var extracted = plan.ExtractedSubtitles.Select(e => FromName(e.FileName)).ToList();
        var deleted = plan.SubtitleDeletions.Select(d => d.FileName).ToList();
        return new Moves(plan, muxIn, extracted, deleted);
    }

    internal static World Apply(World w, Moves moves)
    {
        var deleted = moves.Deleted.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = w.Files.Where(f => !deleted.Contains(f.FileName(MediaPath))).ToList();
        files.AddRange(moves.Extracted.Where(e => files.All(f => f.Suffix != e.Suffix)));

        var result = ConversionPlanner.Plan(w.File.Snapshot, moves.Plan);
        var after = result.Strategy == ConversionPlanner.ConversionStrategy.Skip
            ? w.File
            : PlanConvergenceProperties.Simulate(w.File, moves.Plan, result, w.File.Sidecars());
        after.Path = MediaPath;
        return w with { File = after, Files = files };
    }

    private static string Identity(IMediaTrack t)
    {
        return $"{IsoLanguage.Find(t.LanguageName).Base.Name}|{t.IsForced}|{t.IsHearingImpaired}|{t.IsCommentary}|{SubtitleSidecars.ExtensionFor(t.GetSubtitleCodec()) ?? "image"}";
    }

    // A plan track as it lands: the plan's language and flags where it has an opinion.
    private static string Identity(TrackPlan plan, World w)
    {
        var source = w.File.Snapshot.Tracks.FirstOrDefault(t => t.Index == plan.Index) as IMediaTrack
                     ?? w.File.Sidecars().First(f => f.Index == plan.Index);
        var language = plan.LanguageCode != null && plan.LanguageCode != source.LanguageCode
            ? IsoLanguage.Find(plan.LanguageCode).Name
            : source.LanguageName;
        return $"{IsoLanguage.Find(language).Base.Name}|{plan.IsForced ?? source.IsForced}|{plan.IsHearingImpaired ?? source.IsHearingImpaired}|{plan.IsCommentary ?? source.IsCommentary}|{SubtitleSidecars.ExtensionFor(source.GetSubtitleCodec()) ?? "image"}";
    }

    internal static SubtitleFile FromName(string fileName)
    {
        var parsed = SubtitleSidecars.Parse(MediaPath, "/lib/Movie/" + fileName)
                     ?? throw new AssertFailedException($"planner wrote a name the parser rejects: {fileName}");
        return new SubtitleFile
        {
            Suffix = fileName[Path.GetFileNameWithoutExtension(MediaPath).Length..],
            LanguageCode = parsed.Language.WriteCode ?? parsed.Language.TwoLetterCode,
            LanguageName = parsed.Language.Name,
            IsForced = parsed.Forced,
            IsHearingImpaired = parsed.HearingImpaired,
            Number = parsed.Number,
            Codec = SubtitleSidecars.CodecFor(parsed.Extension).ToString()
        };
    }

    // --- generators ---

    private static Gen<SubtitleFile> SidecarGen()
    {
        return from lang in Gen.Elements("English", "Dutch", "French", "Japanese")
            from forced in Gen.Elements(true, false)
            from sdh in Gen.Elements(true, false)
            from ext in Gen.Elements("srt", "srt", "ass", "sup")
            select FromName(SubtitleSidecars.FileNameFor(MediaPath, IsoLanguage.Find(lang), sdh, forced, 0, ext));
    }

    private static Gen<World> WorldGen()
    {
        return from c in PlanConvergenceProperties.CaseGen()
            from count in Gen.Choose(0, 4)
            from files in Gen.ArrayOf(SidecarGen(), count)
            from location in Gen.Elements(SubtitleLocation.WhereTheyAre, SubtitleLocation.InsideFile, SubtitleLocation.NextToFile)
            from delete in Gen.Elements(true, false)
            let materialized = c.Materialize()
            select Build(materialized.Item1, materialized.Item2, files, location, delete);
    }

    private static World Build(MediaFile file, Profile profile, SubtitleFile[] files, SubtitleLocation location, bool delete)
    {
        file.Path = MediaPath;
        profile.SubtitleSettings.Location = location;
        profile.SubtitleSettings.DeleteMovedSubtitleFiles = delete;
        var unique = new List<SubtitleFile>();
        foreach (var f in files)
        {
            var candidate = f;
            for (var n = 1; unique.Any(u => u.Suffix == candidate.Suffix); n++)
            {
                candidate = FromName(SubtitleSidecars.FileNameFor(MediaPath, IsoLanguage.Find(f.LanguageName), f.IsHearingImpaired, f.IsForced, n,
                    SubtitleSidecars.ExtensionFor(f.GetSubtitleCodec()) ?? "sup"));
            }

            unique.Add(candidate);
        }

        return new World(file, profile, unique);
    }

    private static string Describe(Moves m)
    {
        return $"container=[{string.Join(",", m.Plan.Tracks.Select(t => t.SourcePath == null ? t.Index.ToString() : Path.GetFileName(t.SourcePath)))}] " +
               $"extract=[{string.Join(",", m.Plan.ExtractedSubtitles.Select(e => $"#{e.Index}->{e.FileName}"))}] delete=[{string.Join(",", m.Deleted)}]";
    }

    private static string DescribeWorld(World w)
    {
        return $"location={w.Profile.SubtitleSettings.Location} delete={w.Profile.SubtitleSettings.DeleteMovedSubtitleFiles} container={w.File.Snapshot.ContainerType} original={w.File.OriginalLanguage}\n" +
               $"subs={System.Text.Json.JsonSerializer.Serialize(w.Profile.SubtitleSettings)}\n" +
               $"files=[{string.Join(", ", w.Files.Select(f => f.FileName(MediaPath)))}]\n{PlanConvergenceProperties.Describe(w.File.Snapshot)}";
    }
}
