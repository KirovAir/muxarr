using System.Text.Json;
using Microsoft.Extensions.Logging;
using Muxarr.Core.Api.Models;
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

    private const string MovieUrl = "/api/v3/movie";
    private const string SeriesUrl = "/api/v3/series";
    private const string EpisodesUrl = "/api/v3/episode?seriesId={0}&includeEpisodeFile=true";
    private const string TestUrl = "/api/v3/diskspace";
    private const string NotificationUrl = "/api/v3/notification";

    public async Task<bool> CanConnect(ArrConfig config)
    {
        var result = await Get<List<DiskSpaceResponse>>(config, TestUrl);
        return result != null;
    }

    public async Task<List<SeriesResponse>> SyncSeries(ArrConfig config)
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

    public async Task<List<MovieResponse>> SyncMovies(ArrConfig config)
    {
        var data = await Get<List<MovieResponse>>(config, MovieUrl);
        return data ?? [];
    }

    public async Task<ArrNotification?> FindMuxarrNotification(ArrConfig config)
    {
        var notifications = await Get<List<ArrNotification>>(config, NotificationUrl);
        return notifications?.FirstOrDefault(n =>
            n.Implementation == "Webhook" &&
            n.Name.Equals("Muxarr", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> CreateWebhookNotification(ArrConfig config, string webhookUrl)
    {
        var payload = ArrNotification.CreateMuxarr(webhookUrl);
        var result = await Post(config, NotificationUrl, payload);
        return result;
    }

    public async Task<bool> UpdateWebhookNotification(ArrConfig config, ArrNotification existing, string webhookUrl)
    {
        // Delete and recreate rather than PUT, because our model only maps a subset
        // of fields — PUTting back would lose unmapped fields (tags, extra event flags, etc.)
        var deleted = await Delete(config, $"{NotificationUrl}/{existing.Id}");
        if (!deleted)
        {
            return false;
        }

        return await CreateWebhookNotification(config, webhookUrl);
    }

    public async Task<bool> DeleteWebhookNotification(ArrConfig config, int notificationId)
    {
        return await Delete(config, $"{NotificationUrl}/{notificationId}");
    }

    private async Task<T?> Get<T>(ArrConfig config, string url) where T : class
    {
        if (string.IsNullOrEmpty(config.Url) || string.IsNullOrEmpty(config.ApiKey))
        {
            _logger.LogWarning("No valid {ParentClass} url/apikey was found.", GetType().Name);
            return null;
        }

        var requestUrl = $"{config.Url.Trim().TrimEnd('/')}{url}";

        try
        {
            using var client = _httpClientFactory.CreateClient("Arr");
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

    private async Task<bool> Post<T>(ArrConfig config, string url, T body)
    {
        return await SendJson(config, HttpMethod.Post, url, body);
    }

    private async Task<bool> Delete(ArrConfig config, string url)
    {
        return await SendJson<object?>(config, HttpMethod.Delete, url, null);
    }

    private async Task<bool> SendJson<T>(ArrConfig config, HttpMethod method, string url, T? body)
    {
        if (string.IsNullOrEmpty(config.Url) || string.IsNullOrEmpty(config.ApiKey))
        {
            return false;
        }

        var requestUrl = $"{config.Url.Trim().TrimEnd('/')}{url}";

        try
        {
            using var client = _httpClientFactory.CreateClient("Arr");
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
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending {Method} to {Url}", method, requestUrl);
            return false;
        }
    }
}
