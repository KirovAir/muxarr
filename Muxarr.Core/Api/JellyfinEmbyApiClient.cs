using System.Reflection;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Muxarr.Core.Config;

namespace Muxarr.Core.Api;

public class JellyfinEmbyApiClient : IMediaServerClient
{
    private readonly ILogger<JellyfinEmbyApiClient> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public const string HttpClientName = "JellyfinEmby";

    private const string SystemInfoUrl = "/System/Info";
    private const string VirtualFoldersUrl = "/Library/VirtualFolders";
    private const string ItemsUrl = "/Items";
    private const string UpdatedMediaUrl = "/Library/Media/Updated";
    private const int ItemPageSize = 1000;
    private static readonly string ClientVersion = GetClientVersion();
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public JellyfinEmbyApiClient(ILogger<JellyfinEmbyApiClient> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public Task<bool> CanConnect(IApiCredentials config)
    {
        return Send(config, HttpMethod.Get, SystemInfoUrl);
    }

    public async Task<MediaServerUpdateResult> UpdateMedia(IApiCredentials config, string mediaPath)
    {
        if (string.IsNullOrWhiteSpace(config.Url) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            _logger.LogWarning("No valid media server url/apikey was found.");
            return MediaServerUpdateResult.Failed;
        }

        var normalizedMediaPath = NormalizePath(mediaPath);

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);

            var libraries = await GetLibraries(client, config);
            foreach (var library in libraries
                         .Where(l => MatchesLibrary(l, normalizedMediaPath))
                         .OrderByDescending(l => l.LongestLocationLength))
            {
                var item = await FindItemByPath(client, config, library, normalizedMediaPath);
                if (item?.Id != null && await RefreshItem(client, config, item.Id))
                {
                    return MediaServerUpdateResult.ItemRefresh;
                }
            }

            if (await SendUpdatedMedia(client, config, normalizedMediaPath))
            {
                return MediaServerUpdateResult.PathUpdate;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to update media server for {Path}", mediaPath);
        }

        return MediaServerUpdateResult.Failed;
    }

    private async Task<bool> Send(IApiCredentials config, HttpMethod method, string relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(config.Url) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            _logger.LogWarning("No valid media server url/apikey was found.");
            return false;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = CreateRequest(config, method, relativeUrl);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("{Method} {Url} returned {StatusCode}: {Body}",
                    method, request.RequestUri, response.StatusCode, responseBody);
                return false;
            }

            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error sending {Method} to {Url}", method, relativeUrl);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Request to {Url} timed out", relativeUrl);
            return false;
        }
    }

    private async Task<List<JellyfinEmbyLibrary>> GetLibraries(HttpClient client, IApiCredentials config)
    {
        using var request = CreateRequest(config, HttpMethod.Get, VirtualFoldersUrl);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("GET {Url} returned {StatusCode}: {Body}",
                request.RequestUri, response.StatusCode, responseBody);
            return [];
        }

        var libraries = await response.Content.ReadFromJsonAsync<List<JellyfinEmbyLibrary>>();
        return libraries ?? [];
    }

    private async Task<JellyfinEmbyItem?> FindItemByPath(HttpClient client, IApiCredentials config,
        JellyfinEmbyLibrary library, string normalizedMediaPath)
    {
        if (string.IsNullOrWhiteSpace(library.ItemId))
        {
            return null;
        }

        for (var startIndex = 0;; startIndex += ItemPageSize)
        {
            using var request = CreateRequest(config, HttpMethod.Get, BuildItemsUrl(library.ItemId, startIndex));
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("GET {Url} returned {StatusCode}: {Body}",
                    request.RequestUri, response.StatusCode, responseBody);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<JellyfinEmbyItemsResponse>();
            var items = payload?.Items ?? [];

            var match = items.FirstOrDefault(item => item.Path != null &&
                                                     string.Equals(NormalizePath(item.Path), normalizedMediaPath,
                                                         PathComparison));
            if (match != null)
            {
                return match;
            }

            if (items.Count < ItemPageSize)
            {
                return null;
            }
        }
    }

    private async Task<bool> RefreshItem(HttpClient client, IApiCredentials config, string itemId)
    {
        var query = new Dictionary<string, string>
        {
            ["MetadataRefreshMode"] = "Default",
            ["ImageRefreshMode"] = "None",
            ["ReplaceAllMetadata"] = "false",
            ["ReplaceAllImages"] = "false",
            ["Recursive"] = "true",
            ["RegenerateTrickplay"] = "false"
        };

        using var request = CreateRequest(config, HttpMethod.Post, $"/Items/{Uri.EscapeDataString(itemId)}/Refresh",
            query);
        using var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("POST {Url} returned {StatusCode}: {Body}",
            request.RequestUri, response.StatusCode, responseBody);
        return false;
    }

    private async Task<bool> SendUpdatedMedia(HttpClient client, IApiCredentials config, string normalizedMediaPath)
    {
        using var request = CreateRequest(config, HttpMethod.Post, UpdatedMediaUrl);
        request.Content = JsonContent.Create(new JellyfinEmbyMediaUpdateRequest
        {
            Updates =
            [
                new JellyfinEmbyMediaUpdate
                {
                    Path = normalizedMediaPath,
                    UpdateType = "Modified"
                }
            ]
        });

        using var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("POST {Url} returned {StatusCode}: {Body}",
            request.RequestUri, response.StatusCode, responseBody);
        return false;
    }

    private HttpRequestMessage CreateRequest(IApiCredentials config, HttpMethod method, string relativeUrl,
        IReadOnlyDictionary<string, string>? query = null)
    {
        var request = new HttpRequestMessage(method, BuildUrl(config, relativeUrl, query));
        request.Headers.TryAddWithoutValidation("X-Emby-Token", config.ApiKey.Trim());
        request.Headers.TryAddWithoutValidation("Authorization", BuildAuthorizationHeader(config.ApiKey.Trim()));
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

    private static string BuildItemsUrl(string libraryItemId, int startIndex)
    {
        var query = new Dictionary<string, string>
        {
            ["Recursive"] = "true",
            ["Fields"] = "Path",
            ["EnableImages"] = "false",
            ["ParentId"] = libraryItemId,
            ["EnableTotalRecordCount"] = "false",
            ["Limit"] = ItemPageSize.ToString(),
            ["StartIndex"] = startIndex.ToString()
        };

        return BuildRelativeUrl(ItemsUrl, query);
    }

    private static Uri BuildUrl(IApiCredentials config, string relativeUrl,
        IReadOnlyDictionary<string, string>? query = null)
    {
        var sanitizedBaseUrl = $"{config.Url.Trim().TrimEnd('/')}/";
        if (!Uri.TryCreate(sanitizedBaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new UriFormatException($"Invalid media server URL: '{config.Url}'.");
        }

        var relative = BuildRelativeUrl(relativeUrl, query);
        if (!Uri.TryCreate(baseUri, relative.TrimStart('/'), out var requestUri))
        {
            throw new UriFormatException(
                $"Invalid request URL. Base URL: '{config.Url}', relative URL: '{relativeUrl}'.");
        }

        return requestUri;
    }

    private static string BuildRelativeUrl(string relativeUrl, IReadOnlyDictionary<string, string>? query)
    {
        if (query == null || query.Count == 0)
        {
            return relativeUrl;
        }

        var queryString = string.Join("&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return $"{relativeUrl}?{queryString}";
    }

    private static bool MatchesLibrary(JellyfinEmbyLibrary library, string normalizedMediaPath)
    {
        return library.Locations.Any(location => IsPathWithin(normalizedMediaPath, NormalizePath(location)));
    }

    private static bool IsPathWithin(string path, string parent)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        if (!path.StartsWith(parent, PathComparison))
        {
            return false;
        }

        return path.Length == parent.Length || path[parent.Length] == '/';
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static string BuildAuthorizationHeader(string apiKey)
    {
        var encodedApiKey = Uri.EscapeDataString(apiKey);
        return
            $"MediaBrowser Client=\"Muxarr\", Device=\"Muxarr\", DeviceId=\"muxarr\", Version=\"{ClientVersion}\", Token=\"{encodedApiKey}\"";
    }

    private static string GetClientVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ??
                      Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }

    private sealed class JellyfinEmbyLibrary
    {
        [JsonPropertyName("Name")] public string? Name { get; init; }
        [JsonPropertyName("Locations")] public List<string> Locations { get; init; } = [];
        [JsonPropertyName("ItemId")] public string? ItemId { get; init; }

        public int LongestLocationLength =>
            Locations.Count == 0 ? 0 : Locations.Max(location => NormalizePath(location).Length);
    }

    private sealed class JellyfinEmbyItemsResponse
    {
        [JsonPropertyName("Items")] public List<JellyfinEmbyItem> Items { get; init; } = [];
    }

    private sealed class JellyfinEmbyItem
    {
        [JsonPropertyName("Id")] public string? Id { get; init; }
        [JsonPropertyName("Path")] public string? Path { get; init; }
    }

    private sealed class JellyfinEmbyMediaUpdateRequest
    {
        [JsonPropertyName("Updates")] public List<JellyfinEmbyMediaUpdate> Updates { get; init; } = [];
    }

    private sealed class JellyfinEmbyMediaUpdate
    {
        [JsonPropertyName("Path")] public string Path { get; init; } = string.Empty;
        [JsonPropertyName("UpdateType")] public string UpdateType { get; init; } = "Modified";
    }
}
