using Muxarr.Data.Entities;

namespace Muxarr.Web.Components.Shared.Selection;

// Selection is a set of ids, not entity references: rows are reloaded on every
// page or filter change, and two loads of the same row must count as one.
public abstract class SelectablePaginatedListComponent<T> : PaginatedListComponent<T> where T : class, IHasId
{
    public readonly HashSet<int> SelectedIds = [];

    public async Task OnSelectAll()
    {
        await InvokeStateHasChanged();
    }

    public async Task OnSelect()
    {
        await InvokeStateHasChanged();
    }
}
