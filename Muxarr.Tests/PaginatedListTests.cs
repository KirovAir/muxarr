using Microsoft.Extensions.Caching.Memory;
using Muxarr.Web.Components.Shared;

namespace Muxarr.Tests;

[TestClass]
public class PaginatedListTests
{
    private sealed class FakeList : PaginatedListComponent<int>
    {
        [Filter] private string _flavor = "";

        public int SourceCount { get; set; } = 500;
        public int Queries { get; private set; }

        public string Flavor
        {
            get => _flavor;
            set => _flavor = value;
        }

        public Task Refresh()
        {
            return UpdateList();
        }

        protected override Task UpdateListCore()
        {
            Queries++;
            TotalItems = SourceCount;
            TotalPages = (int)Math.Ceiling(SourceCount / (double)PageSize);
            return Task.CompletedTask;
        }

        public override Task InvokeStateHasChanged()
        {
            return Task.CompletedTask;
        }
    }

    private static FakeList Make()
    {
        return new FakeList { Cache = new MemoryCache(new MemoryCacheOptions()) };
    }

    [TestMethod]
    public async Task ChangedFilter_ResetsToPageOne()
    {
        var list = Make();
        await list.Refresh();
        await list.ChangePage(7);

        list.Flavor = "strict";
        await list.Refresh();

        Assert.AreEqual(1, list.Page);
    }

    [TestMethod]
    public async Task UnchangedFilters_KeepThePage()
    {
        var list = Make();
        await list.Refresh();
        await list.ChangePage(7);

        await list.Refresh();

        Assert.AreEqual(7, list.Page);
    }

    [TestMethod]
    public async Task Search_ResetsToPageOne()
    {
        var list = Make();
        await list.Refresh();
        await list.ChangePage(7);

        await list.Search("bunny");

        Assert.AreEqual(1, list.Page);
    }

    [TestMethod]
    public async Task Sort_ResetsToPageOne()
    {
        var list = Make();
        await list.Refresh();
        await list.ChangePage(7);

        await list.HandleSort("Name");

        Assert.AreEqual(1, list.Page);
    }

    [TestMethod]
    public async Task ChangePageSize_ResetsToPageOne()
    {
        var list = Make();
        await list.Refresh();
        await list.ChangePage(7);

        await list.ChangePageSize(100);

        Assert.AreEqual(1, list.Page);
    }

    [TestMethod]
    public async Task ShrunkData_ClampsToTheLastPage()
    {
        var list = Make();
        await list.Refresh();
        await list.ChangePage(10);

        list.SourceCount = 120;
        var queriesBefore = list.Queries;
        await list.Refresh();

        Assert.AreEqual(3, list.Page, "120 items at 50 per page is 3 pages");
        Assert.AreEqual(queriesBefore + 2, list.Queries, "the clamp requeries on the corrected page");
    }

    [TestMethod]
    public async Task EmptyResult_ClampsToPageOne_WithoutRequery()
    {
        var list = Make();
        await list.Refresh();
        await list.ChangePage(5);

        list.SourceCount = 0;
        var queriesBefore = list.Queries;
        await list.Refresh();

        Assert.AreEqual(1, list.Page);
        Assert.AreEqual(queriesBefore + 1, list.Queries, "an empty set is empty on every page; no requery");
    }

    [TestMethod]
    public async Task FirstQuery_KeepsARestoredPage()
    {
        var list = Make();
        list.Page = 7;

        await list.Refresh();

        Assert.AreEqual(7, list.Page, "a page restored from cached filters must survive the first query");
    }
}
