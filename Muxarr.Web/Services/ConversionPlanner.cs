using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Services;

// Pure diff + strategy selection. Target is expected to be resolved for the
// source container already (see TargetResolver); this method does not mutate
// its inputs.
public static class ConversionPlanner
{
    public enum ConversionStrategy
    {
        Skip,
        MetadataEdit,
        Remux
    }

    public sealed record PlanResult(ConversionStrategy Strategy, ConversionPlan Delta);

    public static PlanResult Plan(MediaSnapshot source, ConversionPlan desired)
    {
        var family = source.ContainerType.ToContainerFamily();
        var delta = ConversionPlanExtensions.Delta(source, desired);
        var hasStructuralChanges = HasStructuralChanges(source, desired);
        var hasFieldChanges = ConversionPlanExtensions.HasChanges(delta);

        ConversionStrategy strategy;
        if (hasStructuralChanges)
        {
            strategy = ConversionStrategy.Remux;
        }
        else if (!hasFieldChanges)
        {
            strategy = ConversionStrategy.Skip;
        }
        else
        {
            strategy = family == ContainerFamily.Matroska
                ? ConversionStrategy.MetadataEdit
                : ConversionStrategy.Remux;
        }

        return new PlanResult(strategy, delta);
    }

    private static bool HasStructuralChanges(MediaSnapshot source, ConversionPlan desired)
    {
        if (source.Tracks.Count != desired.Tracks.Count)
        {
            return true;
        }

        for (var i = 0; i < source.Tracks.Count; i++)
        {
            if (source.Tracks[i].Index != desired.Tracks[i].Index)
            {
                return true;
            }
        }

        return false;
    }
}
