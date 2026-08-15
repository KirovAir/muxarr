using Muxarr.Core.Language;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

// The priority editor's group-aware reordering: an anchor drags its fallbacks
// along and never lands inside another group, a fallback goes wherever it is
// dropped. Rows are written as "German" for an anchor and ">English" for a fallback.
[TestClass]
public class LanguageGroupTests
{
    private static List<LanguagePreference> Rows(params string[] spec)
    {
        return spec
            .Select(s => new LanguagePreference(IsoLanguage.Find(s.TrimStart('>'))) { IsFallback = s[0] == '>' })
            .ToList();
    }

    private static void AssertRows(List<LanguagePreference> languages, params string[] expected)
    {
        CollectionAssert.AreEqual(expected, languages.Select(l => (l.IsFallback ? ">" : "") + l.Name).ToArray());
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
}
