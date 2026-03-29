using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Muxarr.Data;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Components.Shared;

public class DefaultPaginatedListComponent<T>(AppDbContext context, Expression<Func<T, IComparable>>[] searchProperties)
    : PaginatedListComponent<T> where T : class
{
    protected override async Task UpdateListCore()
    {
        var query = context.Set<T>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            query = query.WhereDynamic(searchProperties, SearchTerm);
        }

        var result = await query.Sort(CurrentSortProperty, IsAscending).FindPagedAsync(Page, PageSize, true);
        TotalItems = result.Total;
        TotalPages = result.TotalPages;
        Items = await result.Data.ToListAsync().ConfigureAwait(false);
    }
}