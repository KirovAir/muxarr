using Microsoft.AspNetCore.Components;

namespace Muxarr.Web.Components.Shared.Selection;

public abstract class SelectablePaginatedListComponent<T> : PaginatedListComponent<T> where T : class
{
    public readonly HashSet<T> SelectedItems = new(new EntityComparer<T>());

    [Parameter]
    public EventCallback<IEnumerable<T>> OnSelectionChanged { get; set; }

    public bool ShowMultiSelect { get; set; }

    public async Task OnSelectAll()
    {
        await InvokeStateHasChanged();
        await OnSelectionChanged.InvokeAsync(SelectedItems);
    }

    public async Task OnSelect()
    {
        await InvokeStateHasChanged();
        await OnSelectionChanged.InvokeAsync(SelectedItems);
    }
}

// Todo: check if this works.
public class EntityComparer<T> : IEqualityComparer<T> where T : class
{
    public bool Equals(T? x, T? y)
    {
        if (x == null || y == null)
        {
            return x == y;
        }

        return x == y;
    }

    public int GetHashCode(T obj)
    {
        return obj.GetHashCode();
    }
}
