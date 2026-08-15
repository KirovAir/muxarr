using Muxarr.Data.Entities;

namespace Muxarr.Web.Components.Shared.Selection;

// Selection is a set of ids, not entity references: rows are reloaded on every
// page or filter change, and two loads of the same row must count as one.
public abstract class SelectablePaginatedListComponent<T> : PaginatedListComponent<T> where T : class, IHasId
{
    public readonly HashSet<int> SelectedIds = [];

    private int? _lastClickedId;

    public async Task OnSelectAll()
    {
        await InvokeStateHasChanged();
    }

    // A click toggles the row. With shift held it also toggles, the same way,
    // every row between the previously clicked one and this one.
    public async Task OnRowClick(int id, bool shift)
    {
        var select = !SelectedIds.Contains(id);
        foreach (var target in shift ? RangeTo(id) : [id])
        {
            if (select)
            {
                SelectedIds.Add(target);
            }
            else
            {
                SelectedIds.Remove(target);
            }
        }

        _lastClickedId = id;
        await InvokeStateHasChanged();
    }

    // Either end may have left the page by now (the previous click on another
    // page, this row refreshed away under the click); then there is no range.
    private List<int> RangeTo(int id)
    {
        var ids = Items.Select(i => i.Id).ToList();
        var from = _lastClickedId is { } last ? ids.IndexOf(last) : -1;
        var to = ids.IndexOf(id);
        if (from < 0 || to < 0)
        {
            return [id];
        }

        var start = Math.Min(from, to);
        return ids.GetRange(start, Math.Abs(to - from) + 1);
    }
}
