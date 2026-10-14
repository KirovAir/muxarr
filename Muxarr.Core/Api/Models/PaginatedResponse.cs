namespace Muxarr.Core.Api.Models;

public class PaginatedResponse<T>
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages { get; init; }
    public required List<T> Items { get; init; }
}
