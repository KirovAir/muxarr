namespace Muxarr.Web.Services;

// The files handed to the batch track editor. Ids only: the page reloads the
// rows itself, and a few hundred of them have no business in a query string.
// Empty means the page was reached without a selection, which is its own state.
public class LibrarySelectionState
{
    public HashSet<int> Ids { get; } = [];

    public void Set(IEnumerable<int> ids)
    {
        Ids.Clear();
        foreach (var id in ids)
        {
            Ids.Add(id);
        }
    }
}
