using System.Reflection;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using static Muxarr.Tests.TestData;

namespace Muxarr.Tests;

[TestClass]
public class ConversionPlanExtensionsTests
{
    // If you add a nullable field to TrackPlan and forget to wire it into
    // Delta, this fails with the property name attached.
    [TestMethod]
    public void Delta_EveryNullableField_PropagatesChange()
    {
        var source = Audio(1, "English", "Aac", 2,
            trackName: "English", languageCode: "eng");
        var snapshot = MakeFile(null, source).Snapshot;

        foreach (var field in DiffableFields)
        {
            var plan = new ConversionPlan { Tracks = [source.ToTargetTrack(false)] };
            field.SetValue(plan.Tracks[0], Distinct(field.PropertyType, field.GetValue(plan.Tracks[0])));

            var delta = ConversionPlanExtensions.Delta(snapshot, plan).Tracks[0];

            Assert.IsNotNull(field.GetValue(delta),
                $"Delta missed change on TrackPlan.{field.Name}");
        }
    }

    [TestMethod]
    public void Delta_MatchingFields_AreAllNull()
    {
        var source = Audio(1, "English", trackName: "English", languageCode: "eng");
        var snapshot = MakeFile(null, source).Snapshot;
        var plan = new ConversionPlan { Tracks = [source.ToTargetTrack(false)] };

        var delta = ConversionPlanExtensions.Delta(snapshot, plan).Tracks[0];

        foreach (var field in DiffableFields)
        {
            Assert.IsNull(field.GetValue(delta),
                $"TrackPlan.{field.Name} should be null when source and desired match");
        }
    }

    // HasMetadataChanges (private; reached via CheckHasNonStandardMetadata) must
    // flag every nullable TrackPlan field. Coverage mirrors the Delta test.
    [TestMethod]
    public void CheckHasNonStandardMetadata_EveryNullableField_IsFlagged()
    {
        var source = Audio(1, "English", trackName: "English", languageCode: "eng");
        var file = MakeFile(null, source);
        var profile = MakeProfile();

        foreach (var field in DiffableFields)
        {
            var plan = new ConversionPlan { Tracks = [source.ToTargetTrack(false)] };
            field.SetValue(plan.Tracks[0], Distinct(field.PropertyType, field.GetValue(plan.Tracks[0])));

            Assert.IsTrue(file.CheckHasNonStandardMetadata(profile, plan),
                $"HasMetadataChanges missed change on TrackPlan.{field.Name}");
        }
    }

    [TestMethod]
    public void Delta_PreservesSourcePathOnExternalTrack()
    {
        var source = new MediaSnapshot { Tracks = new List<TrackSnapshot>() };
        var desired = new ConversionPlan
        {
            Tracks = new List<TrackPlan>
            {
                new()
                {
                    Index = 100,
                    Type = MediaTrackType.Subtitles,
                    LanguageCode = "eng",
                    SourcePath = "/media/Elemental.eng.srt"
                }
            }
        };

        var delta = ConversionPlanExtensions.Delta(source, desired);

        Assert.AreEqual("/media/Elemental.eng.srt", delta.Tracks[0].SourcePath);
        Assert.AreEqual(100, delta.Tracks[0].Index);
    }

    private static IEnumerable<PropertyInfo> DiffableFields => typeof(TrackPlan)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanWrite && (Nullable.GetUnderlyingType(p.PropertyType) is not null
                                   || p.PropertyType == typeof(string))
                               && p.Name != nameof(TrackPlan.SourcePath));

    private static object Distinct(Type type, object? current)
    {
        var u = Nullable.GetUnderlyingType(type) ?? type;
        if (u == typeof(bool))
        {
            return !(bool?)(current ?? false)!;
        }

        if (u == typeof(string))
        {
            return (string?)current == "__x" ? "__y" : "__x";
        }

        throw new NotSupportedException(u.Name);
    }
}
