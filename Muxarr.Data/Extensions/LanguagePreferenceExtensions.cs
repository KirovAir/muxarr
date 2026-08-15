using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

// A priority list falls into groups: an anchor (any entry that is not a fallback)
// and the fallbacks directly under it. The editor reorders through these so a
// group never gets split or silently re-parented.
public static class LanguagePreferenceExtensions
{
    /// <summary>
    /// The languages an entry is a fallback for: the ones above it in its group,
    /// in priority order. Empty when the entry is kept unconditionally.
    /// </summary>
    public static List<LanguagePreference> FallbackFor(this List<LanguagePreference> languages, int index)
    {
        var covered = new List<LanguagePreference>();
        if (!languages[index].IsFallback)
        {
            return covered;
        }

        for (var i = index - 1; i >= 0; i--)
        {
            covered.Insert(0, languages[i]);
            if (!languages[i].IsFallback)
            {
                break;
            }
        }

        return covered;
    }

    // Where a drop on targetIndex lands when sourceIndex is dragged, or -1 for a
    // drop inside the dragged block itself. A fallback row may go anywhere,
    // joining a group is exactly what dropping into one means. An anchor block
    // snaps to group boundaries so it never splits another anchor from its
    // fallbacks: down-drags land after the target's whole group, up-drags before
    // its anchor.
    public static int DropTarget(this List<LanguagePreference> languages, int sourceIndex, int targetIndex)
    {
        if (targetIndex >= sourceIndex && targetIndex < sourceIndex + languages.GroupLength(sourceIndex))
        {
            return -1;
        }

        if (languages[sourceIndex].IsFallback)
        {
            return targetIndex;
        }

        var start = languages.GroupStart(targetIndex);
        return targetIndex > sourceIndex ? start + languages.GroupLength(start) - 1 : start;
    }

    // Moves the row at sourceIndex, with its fallbacks if it is an anchor, to
    // where a drop on targetIndex lands.
    public static void Move(this List<LanguagePreference> languages, int sourceIndex, int targetIndex)
    {
        targetIndex = languages.DropTarget(sourceIndex, targetIndex);
        if (targetIndex < 0)
        {
            return;
        }

        var target = languages[targetIndex];
        var length = languages.GroupLength(sourceIndex);

        // A fallback dropped onto an anchor joins that anchor's group whichever
        // direction it came from; inserting above would hand it to the group before.
        var insertAfter = targetIndex > sourceIndex
                          || (languages[sourceIndex].IsFallback && !target.IsFallback);

        var block = languages.GetRange(sourceIndex, length);
        languages.RemoveRange(sourceIndex, length);

        var insertAt = languages.IndexOf(target);
        languages.InsertRange(insertAfter ? insertAt + 1 : insertAt, block);
        languages.AnchorTop();
    }

    // Removing an anchor promotes its first fallback rather than silently
    // re-pointing it at whatever language happens to sit above the hole.
    public static void RemoveEntry(this List<LanguagePreference> languages, int index)
    {
        if (!languages[index].IsFallback && index + 1 < languages.Count)
        {
            languages[index + 1].IsFallback = false;
        }

        languages.RemoveAt(index);
    }

    // The top entry has nothing above it to stand in for.
    public static void AnchorTop(this List<LanguagePreference> languages)
    {
        if (languages.Count > 0)
        {
            languages[0].IsFallback = false;
        }
    }

    // How many rows move together when this one is dragged: an anchor takes its
    // fallbacks along, since they only mean anything directly under it.
    private static int GroupLength(this List<LanguagePreference> languages, int index)
    {
        if (languages[index].IsFallback)
        {
            return 1;
        }

        var length = 1;
        while (index + length < languages.Count && languages[index + length].IsFallback)
        {
            length++;
        }

        return length;
    }

    private static int GroupStart(this List<LanguagePreference> languages, int index)
    {
        while (index > 0 && languages[index].IsFallback)
        {
            index--;
        }

        return index;
    }
}
