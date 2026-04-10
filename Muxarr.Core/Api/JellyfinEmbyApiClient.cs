using System.Reflection;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Muxarr.Core.Api.Models;
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

    public async Task<bool> CanConnect(IApiCredentials config)
    {
        return await Send(config, HttpMethod.Get, SystemInfoUrl);
    }

    public async Task<MediaServerUpdateResult> UpdateMedia(IApiCredentials config, string mediaPath)
    {
        var normalizedMediaPath = NormalizePath(mediaPath);

        var libraries = await Get<List<JellyfinEmbyLibrary>>(config, VirtualFoldersUrl);
        if (libraries == null)
        {
            return MediaServerUpdateResult.Failed;
        }

        foreach (var library in libraries
                     .Where(l => MatchesLibrary(l, normalizedMediaPath))
                     .OrderByDescending(l => l.LongestLocationLength))
        {
            var item = await FindItemByPath(config, library, normalizedMediaPath);
            if (item?.Id != null && await RefreshItem(config, item.Id))
            {
                return MediaServerUpdateResult.ItemRefresh;
            }
        }

        var sent = await SendUpdatedMedia(config, normalizedMediaPath);
        return sent ? MediaServerUpdateResult.PathUpdate : MediaServerUpdateResult.Failed;
    }

    private async Task<JellyfinEmbyItem?> FindItemByPath(IApiCredentials config,
        JellyfinEmbyLibrary library, string normalizedMediaPath)
    {
        if (string.IsNullOrWhiteSpace(library.ItemId))
        {
            return null;
        }

        for (var startIndex = 0;; startIndex += ItemPageSize)
        {
            var payload = await Get<JellyfinEmbyItemsResponse>(config, BuildItemsUrl(library.ItemId, startIndex));
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

    private async Task<bool> RefreshItem(IApiCredentials config, string itemId)
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

        return await Send(config, HttpMethod.Post, $"/Items/{Uri.EscapeDataString(itemId)}/Refresh", query: query);
    }

    private async Task<bool> SendUpdatedMedia(IApiCredentials config, string normalizedMediaPath)
    {
        return await Send(config, HttpMethod.Post, UpdatedMediaUrl, content: JsonContent.Create(
            new JellyfinEmbyMediaUpdateRequest
            {
                Updates =
                [
                    new JellyfinEmbyMediaUpdate
                    {
                        Path = normalizedMediaPath,
                        UpdateType = "Modified"
                    }
                ]
            }));
    }

    private async Task<T?> Get<T>(IApiCredentials config, string relativeUrl,
        IReadOnlyDictionary<string, string>? query = null) where T : class
    {
        if (string.IsNullOrWhiteSpace(config.Url) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            _logger.LogWarning("No valid {ParentClass} url/apikey was found.", GetType().Name);
            return null;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = CreateRequest(config, HttpMethod.Get, relativeUrl, query);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("GET {Url} returned {StatusCode}: {Body}",
                    request.RequestUri, response.StatusCode, responseBody);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error requesting {Url}", relativeUrl);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Request to {Url} timed out", relativeUrl);
            return null;
        }
    }

    private async Task<bool> Send(IApiCredentials config, HttpMethod method, string relativeUrl,
        HttpContent? content = null, IReadOnlyDictionary<string, string>? query = null)
    {
        if (string.IsNullOrWhiteSpace(config.Url) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            _logger.LogWarning("No valid {ParentClass} url/apikey was found.", GetType().Name);
            return false;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = CreateRequest(config, method, relativeUrl, query);
            if (content != null)
            {
                request.Content = content;
            }

            using var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("{Method} {Url} returned {StatusCode}: {Body}",
                method, request.RequestUri, response.StatusCode, responseBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending {Method} to {Url}", method, relativeUrl);
            return false;
        }
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

}
