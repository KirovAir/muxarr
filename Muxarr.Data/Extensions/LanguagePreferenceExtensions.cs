using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

// A priority list falls into groups: an anchor (any entry that starts a new group)
// and everything attached under it. A group is split into tiers, one per fallback
// step, and languages sharing a tier are kept side by side. The editor reorders
// through these so a group never gets split or silently re-parented.
public static class LanguagePreferenceExtensions
{
    /// <summary>
    /// The languages an entry defers to: everything in the tiers above it in its
    /// group, in priority order. Empty for an entry in its group's first tier,
    /// which is kept unconditionally.
    /// </summary>
    public static List<LanguagePreference> FallbackFor(this List<LanguagePreference> languages, int index)
    {
        var start = languages.GroupStart(index);
        var tier = languages.Tier(index);

        return Enumerable.Range(start, index - start)
            .Where(i => languages.Tier(i) < tier)
            .Select(i => languages[i])
            .ToList();
    }

    /// <summary>
    /// Adds a language to a tier of the group starting at <paramref name="groupStart"/>,
    /// as the next tier down when <paramref name="tier"/> is one past the last.
    /// </summary>
    public static void AddToGroup(this List<LanguagePreference> languages, int groupStart, int tier,
        LanguagePreference language)
    {
        var tiers = languages.Groups().FirstOrDefault(g => g[0][0] == languages[groupStart]);
        if (tiers == null)
        {
            return;
        }

        if (tier < tiers.Count)
        {
            language.Relation = LanguageRelation.SameTier;
            languages.Insert(languages.IndexOf(tiers[tier][^1]) + 1, language);
        }
        else
        {
            language.Relation = LanguageRelation.NextTier;
            languages.Insert(languages.IndexOf(tiers[^1][^1]) + 1, language);
        }
    }

    /// <summary>
    /// Removes a language and keeps the list tidy: a group left with one tier is
    /// plain languages again, since a shared tier only means something above a
    /// fallback.
    /// </summary>
    public static void RemoveFromGroup(this List<LanguagePreference> languages, int index)
    {
        languages.RemoveEntry(index);
        languages.Tidy();
    }

    /// <summary>Removes a whole group: a language and everything that falls back from it.</summary>
    public static void RemoveGroup(this List<LanguagePreference> languages, int groupStart)
    {
        languages.RemoveRange(groupStart, languages.GroupLength(groupStart));
        languages.AnchorTop();
    }

    // A group with no fallback tier is split into plain languages.
    private static void Ungroup(this List<LanguagePreference> languages, int groupStart)
    {
        var length = languages.GroupLength(groupStart);
        if (languages.Skip(groupStart).Take(length).All(l => l.Relation != LanguageRelation.NextTier))
        {
            foreach (var language in languages.Skip(groupStart).Take(length))
            {
                language.Relation = LanguageRelation.NewGroup;
            }
        }
    }

    /// <summary>The list as groups of tiers, in order: each group is its tiers, each tier its entries.</summary>
    public static List<List<List<LanguagePreference>>> Groups(this List<LanguagePreference> languages)
    {
        var groups = new List<List<List<LanguagePreference>>>();
        foreach (var pref in languages)
        {
            if (pref.Relation == LanguageRelation.NewGroup || groups.Count == 0)
            {
                groups.Add([[pref]]);
            }
            else if (pref.Relation == LanguageRelation.NextTier)
            {
                groups[^1].Add([pref]);
            }
            else
            {
                groups[^1][^1].Add(pref);
            }
        }

        return groups;
    }

    // Where a drop on targetIndex lands when sourceIndex is dragged: the row the
    // block settles next to, or -1 for a drop inside the dragged block itself.
    public static int DropTarget(this List<LanguagePreference> languages, int sourceIndex, int targetIndex)
    {
        return languages.Landing(sourceIndex, targetIndex)?.Index ?? -1;
    }

    // Moves the row at sourceIndex, with the rest of its block if it heads one, to
    // where a drop on targetIndex lands.
    public static void Move(this List<LanguagePreference> languages, int sourceIndex, int targetIndex)
    {
        var landing = languages.Landing(sourceIndex, targetIndex);
        if (landing == null)
        {
            return;
        }

        var (landingIndex, after) = landing.Value;
        var source = languages[sourceIndex];
        var target = languages[landingIndex];

        // Block first: after the swap below the row reads as a tier head and would take its followers along.
        var block = languages.GetRange(sourceIndex, languages.GroupLength(sourceIndex));

        // Dragging a shared language onto the head of its own tier reorders the
        // pair: the seam above the tier moves to whichever language ends up on top.
        if (source.Relation == LanguageRelation.SameTier && landingIndex == languages.TierStart(sourceIndex))
        {
            (source.Relation, target.Relation) = (target.Relation, source.Relation);
        }

        languages.RemoveRange(sourceIndex, block.Count);

        var insertAt = languages.IndexOf(target);
        languages.InsertRange(after ? insertAt + 1 : insertAt, block);
        languages.AnchorTop();
        languages.Tidy();
    }

    /// <summary>Splits every group without a fallback tier into plain languages.</summary>
    public static void Tidy(this List<LanguagePreference> languages)
    {
        for (var i = 0; i < languages.Count; i++)
        {
            if (languages[i].Relation == LanguageRelation.NewGroup)
            {
                languages.Ungroup(i);
            }
        }
    }

    // A relation describes the seam above a row, so the row that slides up into a
    // hole has to inherit it. Without that, removing a tier's first language would
    // pull the rest of that tier up into the tier above.
    public static void RemoveEntry(this List<LanguagePreference> languages, int index)
    {
        if (index + 1 < languages.Count)
        {
            if (!languages[index].IsAttached)
            {
                languages[index + 1].Relation = LanguageRelation.NewGroup;
            }
            else if (languages[index + 1].Relation == LanguageRelation.SameTier)
            {
                languages[index + 1].Relation = languages[index].Relation;
            }
        }

        languages.RemoveAt(index);
    }

    // The top entry has nothing above it to attach to.
    public static void AnchorTop(this List<LanguagePreference> languages)
    {
        if (languages.Count > 0)
        {
            languages[0].Relation = LanguageRelation.NewGroup;
        }
    }

    // Where a dragged block settles and on which side: a shared language goes
    // anywhere, a tier head snaps to tier edges, an anchor to group edges.
    private static (int Index, bool After)? Landing(this List<LanguagePreference> languages, int sourceIndex,
        int targetIndex)
    {
        if (targetIndex >= sourceIndex && targetIndex < sourceIndex + languages.GroupLength(sourceIndex))
        {
            return null;
        }

        var relation = languages[sourceIndex].Relation;
        var down = targetIndex > sourceIndex;

        if (relation == LanguageRelation.SameTier)
        {
            // Dropped onto its own tier head it goes on top of the tier. Dropped onto
            // any other head it joins that head's tier or group; inserting above
            // would hand it to the one before.
            var ownHead = targetIndex == languages.TierStart(sourceIndex);
            var head = languages[targetIndex].Relation != LanguageRelation.SameTier;
            return (targetIndex, !ownHead && (down || head));
        }

        if (relation == LanguageRelation.NewGroup)
        {
            var start = languages.GroupStart(targetIndex);
            return down ? (start + languages.GroupLength(start) - 1, true) : (start, false);
        }

        var tierStart = languages.TierStart(targetIndex);
        if (!down && languages[tierStart].IsAttached)
        {
            return (tierStart, false);
        }

        // A tier cannot sit above its group's first tier, so an up-drag onto that
        // tier lands right under it instead. Already there: nothing to do or show.
        var tierEnd = tierStart + languages.TierLength(tierStart) - 1;
        return tierEnd == sourceIndex - 1 ? null : (tierEnd, true);
    }

    // Which tier of its group a row sits in: zero for the first, one deeper for
    // every fallback step above it.
    private static int Tier(this List<LanguagePreference> languages, int index)
    {
        var tier = 0;
        for (var i = languages.GroupStart(index); i <= index; i++)
        {
            if (languages[i].Relation == LanguageRelation.NextTier)
            {
                tier++;
            }
        }

        return tier;
    }

    // How many rows move together when this one is dragged: an anchor takes its
    // whole group and a tier head takes its own tier, since the rows under either
    // only mean anything there. A language sharing a tier travels alone.
    private static int GroupLength(this List<LanguagePreference> languages, int index)
    {
        if (languages[index].Relation == LanguageRelation.SameTier)
        {
            return 1;
        }

        if (languages[index].IsAttached)
        {
            return languages.TierLength(index);
        }

        var length = 1;
        while (index + length < languages.Count && languages[index + length].IsAttached)
        {
            length++;
        }

        return length;
    }

    // The rows of the tier starting at index: the head and every language sharing it.
    private static int TierLength(this List<LanguagePreference> languages, int index)
    {
        var length = 1;
        while (index + length < languages.Count && languages[index + length].Relation == LanguageRelation.SameTier)
        {
            length++;
        }

        return length;
    }

    // The first row of the tier a row sits in: the one carrying the seam above it.
    private static int TierStart(this List<LanguagePreference> languages, int index)
    {
        while (index > 0 && languages[index].Relation == LanguageRelation.SameTier)
        {
            index--;
        }

        return index;
    }

    private static int GroupStart(this List<LanguagePreference> languages, int index)
    {
        while (index > 0 && languages[index].IsAttached)
        {
            index--;
        }

        return index;
    }
}
