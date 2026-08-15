using Microsoft.Extensions.Caching.Memory;
using Muxarr.Data.Entities;
using Muxarr.Web.Components.Shared.Selection;

namespace Muxarr.Tests;

// Shift-click range selection on the list checkboxes (issue #62).
[TestClass]
public class RowSelectionTests
{
    private sealed class Row(int id) : IHasId
    {
        public int Id { get; } = id;
    }

    // Stand-in for the page component: no renderer, no data source.
    private sealed class Rows : SelectablePaginatedListComponent<Row>
    {
        protected override Task UpdateListCore()
        {
            return Task.CompletedTask;
        }

        public override Task InvokeStateHasChanged()
        {
            return Task.CompletedTask;
        }
    }

    private static Rows Page(params int[] ids)
    {
        return new Rows
        {
            Cache = new MemoryCache(new MemoryCacheOptions()),
            Items = ids.Select(id => new Row(id)).ToList()
        };
    }

    [TestMethod]
    public async Task ShiftClick_SelectsRangeEitherDirection()
    {
        var rows = Page(1, 2, 3, 4, 5, 6);

        await rows.OnRowClick(2, shift: false);
        await rows.OnRowClick(5, shift: true);
        CollectionAssert.AreEquivalent(new[] { 2, 3, 4, 5 }, rows.SelectedIds.ToArray());

        rows.SelectedIds.Clear();
        await rows.OnRowClick(5, shift: false);
        await rows.OnRowClick(2, shift: true);
        CollectionAssert.AreEquivalent(new[] { 2, 3, 4, 5 }, rows.SelectedIds.ToArray());
    }

    // The clicked row's new state wins for the whole range, so shift-clicking a
    // selected row clears back to the previous click.
    [TestMethod]
    public async Task ShiftClick_OnSelectedRow_DeselectsRange()
    {
        var rows = Page(1, 2, 3, 4, 5, 6);
        await rows.OnRowClick(1, shift: false);
        await rows.OnRowClick(6, shift: true);

        await rows.OnRowClick(3, shift: true);

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, rows.SelectedIds.ToArray());
    }

    [TestMethod]
    public async Task ShiftClick_WithoutAnchorOnPage_TogglesOnlyThatRow()
    {
        var rows = Page(1, 2, 3);
        await rows.OnRowClick(9, shift: false);

        await rows.OnRowClick(3, shift: true);

        CollectionAssert.AreEquivalent(new[] { 9, 3 }, rows.SelectedIds.ToArray());
    }
}
