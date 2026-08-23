using Muxarr.Core.Language;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

// The priority editor's group-aware reordering: an anchor drags its group along
// and never lands inside another one, an attached row goes wherever it is dropped.
// Rows are written as "German" for an anchor, ">English" for the next tier down
// and "+Dutch" for a language sharing the tier above it.
[TestClass]
public class LanguageGroupTests
{
    private static List<LanguagePreference> Rows(params string[] spec)
    {
        return spec
            .Select(s => new LanguagePreference(IsoLanguage.Find(s.TrimStart('>', '+')))
            {
                Relation = s[0] switch
                {
                    '>' => LanguageRelation.NextTier,
                    '+' => LanguageRelation.SameTier,
                    _ => LanguageRelation.NewGroup
                }
            })
            .ToList();
    }

    private static void AssertRows(List<LanguagePreference> languages, params string[] expected)
    {
        CollectionAssert.AreEqual(expected, languages.Select(Spec).ToArray());
    }

    private static string Spec(LanguagePreference pref)
    {
        return pref.Relation switch
        {
            LanguageRelation.NextTier => ">" + pref.Name,
            LanguageRelation.SameTier => "+" + pref.Name,
            _ => pref.Name
        };
    }

    [TestMethod]
    public void Move_AnchorTakesItsFallbacksAlong()
    {
        var languages = Rows("German", ">English", ">Dutch", "Japanese");

        languages.Move(0, 3);

        AssertRows(languages, "Japanese", "German", ">English", ">Dutch");
    }

    [TestMethod]
    public void Move_AnchorDroppedInsideGroup_LandsAfterIt()
    {
        var languages = Rows("Japanese", "German", ">English", ">Dutch");

        languages.Move(0, 2);

        AssertRows(languages, "German", ">English", ">Dutch", "Japanese");
    }

    [TestMethod]
    public void Move_AnchorDraggedUpOntoFallback_LandsAboveItsAnchor()
    {
        var languages = Rows("Japanese", ">English", "German", ">Dutch");

        languages.Move(2, 1);

        AssertRows(languages, "German", ">Dutch", "Japanese", ">English");
    }

    [TestMethod]
    public void Move_FallbackOntoAnchor_JoinsUnderIt()
    {
        var up = Rows("German", ">English", ">Dutch", "Japanese");
        up.Move(2, 0);
        AssertRows(up, "German", ">Dutch", ">English", "Japanese");

        var down = Rows("German", ">Dutch", "Japanese", ">English");
        down.Move(1, 2);
        AssertRows(down, "German", "Japanese", ">Dutch", ">English");
    }

    [TestMethod]
    public void Move_FallbackOntoFallback_TakesItsPlace()
    {
        var languages = Rows("German", ">Dutch", "Japanese", ">English");

        languages.Move(1, 3);

        AssertRows(languages, "German", "Japanese", ">English", ">Dutch");
    }

    [TestMethod]
    public void Move_OntoOwnBlock_IsNoOp()
    {
        var languages = Rows("German", ">English", "Japanese");

        languages.Move(0, 1);

        AssertRows(languages, "German", ">English", "Japanese");
    }

    // The highlight follows the real landing row, not the row under the cursor.
    [TestMethod]
    public void DropTarget_AnchorSnapsToGroupBoundary()
    {
        var languages = Rows("Japanese", "German", ">English", ">Dutch", "French");

        Assert.AreEqual(3, languages.DropTarget(0, 1), "down onto an anchor lands after its last fallback");
        Assert.AreEqual(3, languages.DropTarget(0, 2), "down onto a fallback lands after the group");
        Assert.AreEqual(1, languages.DropTarget(4, 3), "up onto a fallback lands above its anchor");
        Assert.AreEqual(-1, languages.DropTarget(1, 2), "a group cannot be dropped inside itself");
        Assert.AreEqual(2, languages.DropTarget(3, 2), "a fallback goes exactly where it is dropped");
    }

    [TestMethod]
    public void RemoveEntry_AnchorPromotesItsFirstFallback()
    {
        var languages = Rows("German", ">English", ">Dutch");

        languages.RemoveEntry(0);

        AssertRows(languages, "English", ">Dutch");
    }

    [TestMethod]
    public void RemoveEntry_FallbackLeavesGroupIntact()
    {
        var languages = Rows("German", ">English", ">Dutch");

        languages.RemoveEntry(1);

        AssertRows(languages, "German", ">Dutch");
    }

    [TestMethod]
    public void FallbackFor_ListsTheGroupAboveInOrder()
    {
        var languages = Rows("German", ">English", ">Dutch", "Japanese", ">French");

        CollectionAssert.AreEqual(new[] { "German", "English" }, languages.FallbackFor(2).Select(l => l.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Japanese" }, languages.FallbackFor(4).Select(l => l.Name).ToArray());
        Assert.AreEqual(0, languages.FallbackFor(3).Count, "an anchor is a fallback for nothing");
    }

    // A shared tier is one slot: neither language defers to the other, and a
    // fallback under them waits on both.
    [TestMethod]
    public void FallbackFor_SharedTierDefersToNeitherHalf()
    {
        var languages = Rows("Norwegian", "+Bokmål, Norwegian", ">English");

        Assert.AreEqual(0, languages.FallbackFor(1).Count, "a shared tier is kept unconditionally");
        CollectionAssert.AreEqual(new[] { "Norwegian", "Bokmål, Norwegian" },
            languages.FallbackFor(2).Select(l => l.Name).ToArray());
    }

    // A language sharing a lower tier inherits that tier's condition.
    [TestMethod]
    public void FallbackFor_SharedTierBelowTheFirstStillWaits()
    {
        var languages = Rows("German", ">English", "+Dutch");

        CollectionAssert.AreEqual(new[] { "German" }, languages.FallbackFor(1).Select(l => l.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "German" }, languages.FallbackFor(2).Select(l => l.Name).ToArray(),
            "Dutch shares English's tier, so it waits on German and not on English");
    }

    [TestMethod]
    public void Move_AnchorTakesASharedTierAlong()
    {
        var languages = Rows("German", "+Dutch", ">English", "Japanese");

        languages.Move(0, 3);

        AssertRows(languages, "Japanese", "German", "+Dutch", ">English");
    }

    [TestMethod]
    public void RemoveEntry_AnchorPromotesASharedTier()
    {
        var languages = Rows("German", "+Dutch", ">English");

        languages.RemoveEntry(0);

        AssertRows(languages, "Dutch", ">English");
    }

    // The relation sits on the seam above a row, so pulling out a tier's first
    // language has to hand that seam on, or the rest of the tier moves up one.
    [TestMethod]
    public void RemoveEntry_TierHeadHandsTheSeamToItsTier()
    {
        var languages = Rows("German", ">English", "+Dutch");

        languages.RemoveEntry(1);

        AssertRows(languages, "German", ">Dutch");
    }

    [TestMethod]
    public void Move_TierHeadTakesItsTierAlong()
    {
        var languages = Rows("German", ">English", "+Dutch", "Japanese");

        languages.Move(1, 3);

        AssertRows(languages, "German", "Japanese", ">English", "+Dutch");
    }

    [TestMethod]
    public void Move_TierCannotBeDroppedInsideItself()
    {
        var languages = Rows("German", ">English", "+Dutch");

        Assert.AreEqual(-1, languages.DropTarget(1, 2));
    }

    // Dragging the second language of a tier above the first reorders the pair
    // rather than splitting them across two tiers.
    [TestMethod]
    public void Move_ReorderingInsideATierKeepsTheTier()
    {
        var languages = Rows("German", ">English", "+Dutch");

        languages.Move(2, 1);

        AssertRows(languages, "German", ">Dutch", "+English");
    }

    [TestMethod]
    public void Move_SharedLanguageOntoItsOwnAnchor_TakesTheAnchorsPlace()
    {
        var languages = Rows("German", "+Dutch", ">English");

        languages.Move(1, 0);

        AssertRows(languages, "Dutch", "+German", ">English");
    }

    // A shared language dropped onto a plain anchor lands under it as a plain
    // row, since a shared tier only means something above a fallback.
    [TestMethod]
    public void Move_SharedLanguageOntoAnotherAnchor_LandsAsAPlainRow()
    {
        var languages = Rows("Japanese", "German", "+Dutch", ">English");

        languages.Move(2, 0);

        AssertRows(languages, "Japanese", "Dutch", "German", ">English");
    }

    // A fallback never lands between a language and the ones sharing its tier,
    // which would silently push them down into the fallback's tier.
    [TestMethod]
    public void Move_FallbackOntoAnchor_LandsUnderTheAnchorsWholeTier()
    {
        var languages = Rows("German", "+Dutch", ">English");

        languages.Move(2, 0);

        AssertRows(languages, "German", "+Dutch", ">English");
    }

    [TestMethod]
    public void Move_FallbackOntoSharedLanguage_LandsUnderItsTier()
    {
        var languages = Rows("German", "+Dutch", ">English", ">French");

        languages.Move(3, 1);

        AssertRows(languages, "German", "+Dutch", ">French", ">English");
    }

    // A shared language dropped onto a tier head joins that tier whichever way
    // it came from, just like dropping onto an anchor joins that group.
    [TestMethod]
    public void Move_SharedLanguageUpOntoTierHead_JoinsThatTier()
    {
        var languages = Rows("German", ">English", ">Dutch", "+French");

        languages.Move(3, 1);

        AssertRows(languages, "German", ">English", "+French", ">Dutch");
    }

    // A fallback already sitting right under its group's first tier has nowhere
    // to go when dropped on that tier, so nothing is highlighted either.
    [TestMethod]
    public void DropTarget_FallbackAlreadyUnderTheFirstTier_IsNoDrop()
    {
        var languages = Rows("German", "+Dutch", ">English");

        Assert.AreEqual(-1, languages.DropTarget(2, 0));
        Assert.AreEqual(-1, languages.DropTarget(2, 1));
    }

    // --- Editing a fallback set ---

    [TestMethod]
    public void AddToGroup_FillsATierOrOpensTheNextOne()
    {
        var languages = Rows("Norwegian", "Japanese");

        languages.AddToGroup(0, 0, new LanguagePreference(IsoLanguage.Find("nob")));
        languages.AddToGroup(0, 1, new LanguagePreference(IsoLanguage.Find("English")));
        languages.AddToGroup(0, 1, new LanguagePreference(IsoLanguage.Find("Dutch")));

        AssertRows(languages, "Norwegian", "+Bokmål, Norwegian", ">English", "+Dutch", "Japanese");
    }

    // Taking the last fallback away leaves plain languages, not a shared tier
    // that means nothing on its own.
    [TestMethod]
    public void RemoveFromGroup_WithoutAFallbackLeft_SplitsTheTier()
    {
        var languages = Rows("Norwegian", "+Bokmål, Norwegian", ">English", "Japanese");

        languages.RemoveFromGroup(2);

        AssertRows(languages, "Norwegian", "Bokmål, Norwegian", "Japanese");
    }

    // Dragging a set's only fallback away leaves the preferred languages as plain rows.
    [TestMethod]
    public void Move_LastFallbackOutOfASet_SplitsWhatIsLeft()
    {
        var languages = Rows("Norwegian", "+Bokmål, Norwegian", ">English", "Japanese");

        languages.Move(2, 3);

        AssertRows(languages, "Norwegian", "Bokmål, Norwegian", "Japanese", ">English");
    }

    [TestMethod]
    public void RemoveGroup_TakesTheFallbacksAlong()
    {
        var languages = Rows("German", ">English", "+Dutch", "Japanese");

        languages.RemoveGroup(0);

        AssertRows(languages, "Japanese");
    }

    [TestMethod]
    public void Move_FallbackDraggedDownOntoSharedTier_LandsAfterIt()
    {
        var languages = Rows("German", ">English", "Japanese", "+Spanish", ">Italian");

        languages.Move(1, 2);

        AssertRows(languages, "German", "Japanese", "+Spanish", ">English", ">Italian");
    }
}
