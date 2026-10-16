using FsCheck;
using FsCheck.Fluent;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Properties;

/// <summary>
/// Executable model of docs/design-subtitle-files.html, section 3 and rules 2 to
/// 5. The location rules run on top of the real GetAllowedTracks; the directory
/// is a list. When the feature is built, PlanWithLocation is replaced by the
/// real planner and these properties stay.
/// </summary>
[TestClass]
public class SubtitleLocationModelProperties
{
    internal enum Location
    {
        WhereTheyAre,
        InsideFile,
        NextToFile
    }

    internal sealed record Sidecar(string Language, bool Forced, bool Sdh, string Ext, int Number)
    {
        // The write grammar: number before the language, as the arrs do.
        public string Name => $"Movie{(Number > 0 ? $".{Number}" : "")}.{IsoLanguage.Find(Language).TwoLetterCode}{(Sdh ? ".sdh" : "")}{(Forced ? ".forced" : "")}.{Ext}";
        public bool IsText => Ext != "sup";
        // A file never carries commentary; an embedded commentary track is a different subtitle.
        public string Identity => $"{Language}|{Forced}|{Sdh}|False|{Format}";
        public string Format => Ext == "sup" ? "image" : Ext;
    }

    // Kept holds the embedded tracks that stay in the container, Resolved their
    // state after the profile's language resolution, which the writer persists.
    internal sealed record Moves(List<int> Kept, List<Sidecar> MuxIn, List<(TrackSnapshot Track, Sidecar Target)> Extract,
        List<Sidecar> Delete, Dictionary<int, TrackSnapshot> Resolved);

    internal sealed record World(MediaFile File, Profile Profile, List<Sidecar> Files, Location Mode, bool DeleteMoved);

    [TestMethod]
    public void Model_Is_Deterministic()
    {
        Check.One(PlanConvergenceProperties.Config, Prop.ForAll(Arb.From(WorldGen()), w =>
        {
            Assert.AreEqual(Describe(PlanWithLocation(w)), Describe(PlanWithLocation(w)), DescribeWorld(w));
        }));
    }

    [TestMethod]
    public void Where_They_Are_Never_Moves_Or_Deletes()
    {
        Check.One(PlanConvergenceProperties.Config, Prop.ForAll(Arb.From(WorldGen()), w =>
        {
            var moves = PlanWithLocation(w with { Mode = Location.WhereTheyAre });
            Assert.AreEqual(0, moves.MuxIn.Count + moves.Extract.Count + moves.Delete.Count, DescribeWorld(w));

            // And the container plan is the one the plain profile gives.
            var plain = w.File.Snapshot.Tracks.Where(t => t.Type == MediaTrackType.Subtitles).ToList()
                .GetAllowedTracks(w.Profile.SubtitleSettings, w.File.OriginalLanguage).Select(t => t.Index).ToList();
            CollectionAssert.AreEqual(plain, moves.Kept, DescribeWorld(w));
        }));
    }

    [TestMethod]
    public void One_Conversion_Converges_And_Loses_Nothing()
    {
        Check.One(PlanConvergenceProperties.Config, Prop.ForAll(Arb.From(WorldGen()), w =>
        {
            var moves = PlanWithLocation(w);
            var after = Apply(w, moves);
            var again = PlanWithLocation(after);
            var detail = $"{DescribeWorld(w)}\n\nmoves:\n{Describe(moves)}\n\nafter:\n{DescribeWorld(after)}\n\nsecond moves:\n{Describe(again)}";

            // Rule 2: nothing deleted unless the switch is on, and then only a moved or replaced file.
            if (!w.DeleteMoved)
            {
                Assert.AreEqual(0, moves.Delete.Count, "deleted with the switch off\n" + detail);
                Assert.IsTrue(w.Files.All(f => after.Files.Contains(f)), "a file vanished with the switch off\n" + detail);
            }
            else
            {
                var moved = moves.MuxIn.Select(m => m.Identity).Concat(moves.Extract.Select(e => e.Target.Identity)).ToHashSet();
                Assert.IsTrue(moves.Delete.All(d => moved.Contains(d.Identity)), "deleted a file that was not just moved or replaced\n" + detail);
            }

            // Every kept subtitle is somewhere afterwards.
            foreach (var index in moves.Kept)
            {
                var track = moves.Resolved[index];
                var inContainer = after.File.Snapshot.Tracks.Any(t => Identity(t) == Identity(track));
                var inFiles = after.Files.Any(f => f.Identity == Identity(track));
                Assert.IsTrue(inContainer || inFiles, $"kept subtitle #{index} is gone\n{detail}");
            }

            foreach (var muxed in moves.MuxIn)
            {
                Assert.IsTrue(after.File.Snapshot.Tracks.Any(t => Identity(t) == muxed.Identity), $"muxed-in {muxed.Name} not in container\n{detail}");
            }

            // Converges: the second pass has no moves and no deletions. The one
            // exception is the undetermined rule firing once a removal leaves a
            // single untagged subtitle, which then gets a name and a file; the
            // third pass must be quiet.
            if (again.MuxIn.Count + again.Extract.Count + again.Delete.Count > 0)
            {
                Assert.IsTrue(PlanConvergenceProperties.UndeterminedRuleJustBecameApplicable(w.File, after.File, w.Profile),
                    "second conversion still moves\n" + detail);
                var third = PlanWithLocation(Apply(after, again));
                Assert.AreEqual(0, third.MuxIn.Count + third.Extract.Count + third.Delete.Count, "third conversion still moves\n" + detail);
            }
        }));
    }

    // --- the model (design section 3) ---

    internal static Moves PlanWithLocation(World w)
    {
        var family = w.File.Snapshot.ContainerType.ToContainerFamily();
        var mode = w.Mode == Location.InsideFile && family != ContainerFamily.Matroska ? Location.WhereTheyAre : w.Mode;
        var embedded = w.File.Snapshot.Tracks.Where(t => t.Type == MediaTrackType.Subtitles).Select(t => t.ToSnapshot()).ToList();

        // Step 3's undetermined rule counts embedded tracks only, so resolve it here
        // before sidecars join the list and change the count.
        if (embedded.Count == 1 && embedded[0].ShouldResolveUndetermined(w.Profile.SubtitleSettings, 1, w.File.OriginalLanguage))
        {
            var iso = IsoLanguage.Find(w.File.OriginalLanguage!);
            embedded[0].LanguageName = iso.Name;
            embedded[0].LanguageCode = iso.WriteCode ?? embedded[0].LanguageCode;
        }

        var embeddedIdentities = embedded.Select(Identity).ToHashSet();

        // Step 2: candidates. Sidecars only in "Inside the file", and only those not already embedded (rule 3).
        var candidates = embedded.ToList();
        var sidecarByIndex = new Dictionary<int, Sidecar>();
        if (mode == Location.InsideFile)
        {
            var i = 0;
            foreach (var f in w.Files.Where(f => f.IsText && !embeddedIdentities.Contains(f.Identity)))
            {
                var track = TestData.Sub(1000 + i++, f.Language, CodecFor(f.Ext), f.Forced, f.Sdh);
                sidecarByIndex[track.Index] = f;
                candidates.Add(track);
            }
        }

        // Step 3: the language rules, untouched.
        var kept = candidates.GetAllowedTracks(w.Profile.SubtitleSettings, w.File.OriginalLanguage);

        // Step 4: split by destination.
        var keptEmbedded = new List<int>();
        var muxIn = new List<Sidecar>();
        var extract = new List<(TrackSnapshot, Sidecar)>();
        var delete = new List<Sidecar>();
        var taken = w.Files.Select(f => f.Name).ToHashSet();
        foreach (var track in kept)
        {
            if (sidecarByIndex.TryGetValue(track.Index, out var sidecar))
            {
                muxIn.Add(sidecar);
                if (w.DeleteMoved)
                {
                    delete.Add(sidecar);
                }

                continue;
            }

            var ext = TextExtension(track);
            var known = IsoLanguage.Find(track.LanguageName) is var iso && iso != IsoLanguage.Unknown && iso.Name != IsoLanguage.UndeterminedName;
            if (mode != Location.NextToFile || ext == null || !known)
            {
                keptEmbedded.Add(track.Index);
                continue;
            }

            // Rule 4: always extract; replace the like-for-like file (same language,
            // flags and format) with the switch on, number with it off.
            var target = new Sidecar(track.LanguageName, track.IsForced, track.IsHearingImpaired, ext, 0);
            if (w.DeleteMoved && w.Files.FirstOrDefault(f => f.Name == target.Name) is { } replaced && !delete.Contains(replaced))
            {
                delete.Add(replaced);
                taken.Remove(replaced.Name);
            }

            for (var n = 1; taken.Contains(target.Name); n++)
            {
                target = target with { Number = n };
            }

            taken.Add(target.Name);
            extract.Add((track, target));
        }

        return new Moves(keptEmbedded, muxIn, extract, delete, embedded.ToDictionary(t => t.Index));
    }

    // The directory and the container after the conversion and a rescan.
    internal static World Apply(World w, Moves moves)
    {
        var files = w.Files.Where(f => !moves.Delete.Contains(f)).ToList();
        files.AddRange(moves.Extract.Select(e => e.Target));

        var tracks = w.File.Snapshot.Tracks.Where(t => t.Type != MediaTrackType.Subtitles).Select(t => t.ToSnapshot()).ToList();
        tracks.AddRange(moves.Kept.Select(i => moves.Resolved[i].ToSnapshot()));
        tracks.AddRange(moves.MuxIn.Select(m => TestData.Sub(0, m.Language, CodecFor(m.Ext), m.Forced, m.Sdh)));
        for (var i = 0; i < tracks.Count; i++)
        {
            tracks[i].Index = i;
        }

        var file = TestData.MakeFile(w.File.OriginalLanguage, tracks.ToArray());
        file.Snapshot.ContainerType = w.File.Snapshot.ContainerType;
        return w with { File = file, Files = files };
    }

    internal static string Identity(IMediaTrack t)
    {
        var ext = TextExtension(t);
        return $"{t.LanguageName}|{t.IsForced}|{t.IsHearingImpaired}|{t.IsCommentary}|{ext ?? "image"}";
    }

    private static string? TextExtension(IMediaTrack t)
    {
        return t.GetSubtitleCodec() switch
        {
            SubtitleCodec.Srt or SubtitleCodec.MovText => "srt",
            SubtitleCodec.Ass => "ass",
            SubtitleCodec.WebVtt => "vtt",
            _ => null
        };
    }

    private static string CodecFor(string ext)
    {
        return ext switch
        {
            "ass" => nameof(SubtitleCodec.Ass),
            "vtt" => nameof(SubtitleCodec.WebVtt),
            "sup" => nameof(SubtitleCodec.Pgs),
            _ => nameof(SubtitleCodec.Srt)
        };
    }

    // --- generators ---

    private static Gen<Sidecar> SidecarGen()
    {
        return from lang in Gen.Elements("English", "Dutch", "French", "Japanese")
            from forced in Gen.Elements(true, false)
            from sdh in Gen.Elements(true, false)
            from ext in Gen.Elements("srt", "srt", "ass", "sup")
            select new Sidecar(lang, forced, sdh, ext, 0);
    }

    private static Gen<World> WorldGen()
    {
        return from c in PlanConvergenceProperties.CaseGen()
            from count in Gen.Choose(0, 4)
            from files in Gen.ArrayOf(SidecarGen(), count)
            from mode in Gen.Elements(Location.WhereTheyAre, Location.InsideFile, Location.NextToFile)
            from delete in Gen.Elements(true, false)
            let materialized = c.Materialize()
            select new World(materialized.Item1, materialized.Item2, Dedupe(files), mode, delete);
    }

    // Two files cannot share a name; number the later one the way the arrs do.
    private static List<Sidecar> Dedupe(IEnumerable<Sidecar> files)
    {
        var result = new List<Sidecar>();
        foreach (var f in files)
        {
            var s = f;
            for (var n = 1; result.Any(r => r.Name == s.Name); n++)
            {
                s = f with { Number = n };
            }

            result.Add(s);
        }

        return result;
    }

    private static string Describe(Moves m)
    {
        return $"kept=[{string.Join(",", m.Kept)}] muxIn=[{string.Join(",", m.MuxIn.Select(x => x.Name))}] " +
               $"extract=[{string.Join(",", m.Extract.Select(e => $"#{e.Track.Index}->{e.Target.Name}"))}] delete=[{string.Join(",", m.Delete.Select(x => x.Name))}]";
    }

    private static string DescribeWorld(World w)
    {
        return $"mode={w.Mode} delete={w.DeleteMoved} container={w.File.Snapshot.ContainerType} original={w.File.OriginalLanguage}\n" +
               $"subs={System.Text.Json.JsonSerializer.Serialize(w.Profile.SubtitleSettings)}\n" +
               $"files=[{string.Join(", ", w.Files.Select(f => f.Name))}]\n{PlanConvergenceProperties.Describe(w.File.Snapshot)}";
    }
}
