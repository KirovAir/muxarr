namespace Muxarr.Core.Api.Models;

public class ConversionResponse
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string State { get; init; }
    public int Progress { get; init; }
    public long SizeBefore { get; init; }
    public long SizeAfter { get; init; }
    public long SizeDifference { get; init; }
    public string? FilePath { get; init; }
    public bool IsCustomConversion { get; init; }
    public DateTime? StartedDate { get; init; }
    public DateTime CreatedDate { get; init; }
    public DateTime UpdatedDate { get; init; }

    public static PaginatedResponse<ConversionResponse> Example => new()
    {
        Page = 1,
        PageSize = 25,
        TotalItems = 1,
        TotalPages = 1,
        Items =
        [
            new ConversionResponse
            {
                Id = 42,
                Name = "Movie Title",
                State = "Completed",
                Progress = 100,
                SizeBefore = 5368709120,
                SizeAfter = 3758096384,
                SizeDifference = 1610612736,
                FilePath = "/media/movies/movie.mkv",
                IsCustomConversion = false,
                StartedDate = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc),
                CreatedDate = new DateTime(2026, 8, 20, 9, 55, 0, DateTimeKind.Utc),
                UpdatedDate = new DateTime(2026, 8, 20, 10, 5, 0, DateTimeKind.Utc)
            }
        ]
    };
}
