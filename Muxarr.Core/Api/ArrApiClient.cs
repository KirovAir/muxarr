using System.Text.Json;
using Microsoft.Extensions.Logging;
using Muxarr.Core.Api.Models;
using Muxarr.Core.Config;
using Muxarr.Core.Utilities;

namespace Muxarr.Core.Api;

public class ArrApiClient
{
    private readonly ILogger<ArrApiClient> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public ArrApiClient(ILogger<ArrApiClient> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public const string HttpClientName = "Arr";

    private const string MovieUrl = "/api/v3/movie";
    private const string SeriesUrl = "/api/v3/series";
    private const string EpisodesUrl = "/api/v3/episode?seriesId={0}&includeEpisodeFile=true";
    private const string TestUrl = "/api/v3/diskspace";
    private const string NotificationUrl = "/api/v3/notification";
    private const string EpisodeFilesUrl = "/api/v3/episodefile?seriesId={0}";
    private const string CommandUrl = "/api/v3/command";
    private const string RenameUrl = "/api/v3/rename?{0}={1}";

    public async Task<bool> CanConnect(IApiCredentials config)
    {
        var result = await Get<List<DiskSpaceResponse>>(config, TestUrl);
        return result != null;
    }

    public async Task<List<SeriesResponse>> SyncSeries(IApiCredentials config)
    {
        var data = await Get<List<SeriesResponse>>(config, SeriesUrl);
        if (data == null)
        {
            return [];
        }

        var epResult = new List<SeriesResponse>();
        foreach (var serie in data)
        {
            var url = string.Format(EpisodesUrl, serie.Id);
            var episodes = await Get<List<EpisodeResponse>>(config, url);
            if (episodes == null)
            {
                continue;
            }

            foreach (var episode in episodes)
            {
                if (!episode.HasFile)
                {
                    continue;
                }

                // Prefer full path, fall back to constructing from series path + relative path
                var episodePath = episode.EpisodeFile.Path;
                if (string.IsNullOrEmpty(episodePath) && !string.IsNullOrEmpty(episode.EpisodeFile.RelativePath))
                {
                    episodePath = Path.Combine(serie.Path, episode.EpisodeFile.RelativePath);
                }

                if (string.IsNullOrEmpty(episodePath))
                {
                    _logger.LogWarning("No file path for {Title} S{Season:D2}E{Episode:D2}",
                        serie.Title, episode.SeasonNumber, episode.EpisodeNumber);
                    continue;
                }

                var epSeries = new SeriesResponse
                {
                    Id = episode.Id,
                    OriginalLanguage = serie.OriginalLanguage,
                    Title = $"{serie.Title} S{episode.SeasonNumber:D2}E{episode.EpisodeNumber:D2}",
                    Path = episodePath
                };
                epResult.Add(epSeries);
            }
        }

        return epResult;
    }

    public async Task<List<MovieResponse>> SyncMovies(IApiCredentials config)
    {
        var data = await Get<List<MovieResponse>>(config, MovieUrl);
        return data ?? [];
    }

    public Task<List<SeriesResponse>?> GetSeries(IApiCredentials config)
    {
        return Get<List<SeriesResponse>>(config, SeriesUrl);
    }

    public Task<SeriesResponse?> GetSeries(IApiCredentials config, int seriesId)
    {
        return Get<SeriesResponse>(config, $"{SeriesUrl}/{seriesId}");
    }

    public Task<List<EpisodeFileResponse>?> GetEpisodeFiles(IApiCredentials config, int seriesId)
    {
        return Get<List<EpisodeFileResponse>>(config, string.Format(EpisodeFilesUrl, seriesId));
    }

    public Task<List<MovieResponse>?> GetMovies(IApiCredentials config)
    {
        return Get<List<MovieResponse>>(config, MovieUrl);
    }

    public Task<MovieResponse?> GetMovie(IApiCredentials config, int movieId)
    {
        return Get<MovieResponse>(config, $"{MovieUrl}/{movieId}");
    }

    public Task<EpisodeResponse?> GetEpisode(IApiCredentials config, int episodeId)
    {
        return Get<EpisodeResponse>(config, $"/api/v3/episode/{episodeId}");
    }

    // Rescans the file's series or movie so the arr picks up the new tracks.
    public Task<ArrCommand?> Rescan(IApiCredentials config, ArrFile file)
    {
        return file.IsMovie
            ? Post<object, ArrCommand>(config, CommandUrl, new { name = "RescanMovie", movieId = file.ParentId })
            : Post<object, ArrCommand>(config, CommandUrl, new { name = "RescanSeries", seriesId = file.ParentId });
    }

    // What the arr would rename the file to under its own naming format, if anything.
    public async Task<RenamePreview?> GetRename(IApiCredentials config, ArrFile file)
    {
        var url = string.Format(RenameUrl, file.IsMovie ? "movieId" : "seriesId", file.ParentId);
        var previews = await Get<List<RenamePreview>>(config, url);
        return previews?.FirstOrDefault(p => (file.IsMovie ? p.MovieFileId : p.EpisodeFileId) == file.FileId);
    }

    public Task<ArrCommand?> Rename(IApiCredentials config, ArrFile file)
    {
        return file.IsMovie
            ? Post<object, ArrCommand>(config, CommandUrl, new { name = "RenameFiles", movieId = file.ParentId, files = new[] { file.FileId } })
            : Post<object, ArrCommand>(config, CommandUrl, new { name = "RenameFiles", seriesId = file.ParentId, files = new[] { file.FileId } });
    }

    public Task<ArrCommand?> GetCommand(IApiCredentials config, int commandId)
    {
        return Get<ArrCommand>(config, $"{CommandUrl}/{commandId}");
    }

    public async Task<ArrNotification?> FindMuxarrNotification(IApiCredentials config)
    {
        var notifications = await Get<List<ArrNotification>>(config, NotificationUrl);
        return notifications?.FirstOrDefault(n =>
            n.Implementation == "Webhook" &&
            n.Name.Equals("Muxarr", StringComparison.OrdinalIgnoreCase));
    }

    // Returns the arr's own error message when it refuses, null when the webhook is in place.
    public Task<string?> CreateWebhookNotification(IApiCredentials config, string webhookUrl)
    {
        return Post(config, NotificationUrl, ArrNotification.CreateMuxarr(webhookUrl));
    }

    public async Task<string?> UpdateWebhookNotification(IApiCredentials config, ArrNotification existing,
        string webhookUrl)
    {
        // Delete and recreate rather than PUT, because our model only maps a subset
        // of fields; PUTting back would lose unmapped fields (tags, extra event flags, etc.)
        var error = await Delete(config, $"{NotificationUrl}/{existing.Id}");
        return error ?? await CreateWebhookNotification(config, webhookUrl);
    }

    public async Task<bool> DeleteWebhookNotification(IApiCredentials config, int notificationId)
    {
        return await Delete(config, $"{NotificationUrl}/{notificationId}") == null;
    }

    private async Task<T?> Get<T>(IApiCredentials config, string url) where T : class
    {
        if (string.IsNullOrEmpty(config.Url) || string.IsNullOrEmpty(config.ApiKey))
        {
            _logger.LogWarning("No valid {ParentClass} url/apikey was found.", GetType().Name);
            return null;
        }

        var requestUrl = $"{config.Url.Trim().TrimEnd('/')}{url}";

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("X-Api-Key", config.ApiKey.Trim());

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Request to {Url} returned {StatusCode}", requestUrl, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            return JsonHelper.Deserialize<T>(content);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error requesting {Url}", requestUrl);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Request to {Url} timed out", requestUrl);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize response from {Url}", requestUrl);
            return null;
        }
    }

    private Task<string?> Post<T>(IApiCredentials config, string url, T body)
    {
        return SendJson(config, HttpMethod.Post, url, body);
    }

    private async Task<TResponse?> Post<T, TResponse>(IApiCredentials config, string url, T body) where TResponse : class
    {
        if (string.IsNullOrEmpty(config.Url) || string.IsNullOrEmpty(config.ApiKey))
        {
            return null;
        }

        var requestUrl = $"{config.Url.Trim().TrimEnd('/')}{url}";

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Add("X-Api-Key", config.ApiKey.Trim());
            var json = JsonSerializer.Serialize(body, JsonHelper.Settings);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("POST {Url} returned {StatusCode}: {Body}", requestUrl, response.StatusCode, content);
                return null;
            }

            return JsonHelper.Deserialize<TResponse>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending POST to {Url}", requestUrl);
            return null;
        }
    }

    private Task<string?> Delete(IApiCredentials config, string url)
    {
        return SendJson<object?>(config, HttpMethod.Delete, url, null);
    }

    // Null when the arr accepted it, otherwise what went wrong in the arr's own words.
    private async Task<string?> SendJson<T>(IApiCredentials config, HttpMethod method, string url, T? body)
    {
        if (string.IsNullOrEmpty(config.Url) || string.IsNullOrEmpty(config.ApiKey))
        {
            return "no URL or API key configured";
        }

        var requestUrl = $"{config.Url.Trim().TrimEnd('/')}{url}";

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(method, requestUrl);
            request.Headers.Add("X-Api-Key", config.ApiKey.Trim());

            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, JsonHelper.Settings);
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("{Method} {Url} returned {StatusCode}: {Body}",
                    method, requestUrl, response.StatusCode, responseBody);
                return ArrErrorMessage(responseBody) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending {Method} to {Url}", method, requestUrl);
            return ex.Message;
        }
    }

    // Sonarr and Radarr answer errors with {"message": "..."}; the webhook test failing lands here too.
    private static string? ArrErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
