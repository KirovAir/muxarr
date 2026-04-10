using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Muxarr.Core.Config;

namespace Muxarr.Core.Api;

public class PlexApiClient : IMediaServerClient
{
    private readonly ILogger<PlexApiClient> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public const string HttpClientName = "Plex";

    private const string IdentityUrl = "/identity";
    private const string LibrarySectionsUrl = "/library/sections";
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public PlexApiClient(ILogger<PlexApiClient> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> CanConnect(IApiCredentials config)
    {
        if (string.IsNullOrWhiteSpace(config.Url) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return false;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = CreateRequest(config, HttpMethod.Get, IdentityUrl);
            using var response = await client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (UriFormatException ex)
        {
            _logger.LogWarning(ex, "Invalid Plex URL configured: {Url}", config.Url);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Failed to connect to Plex at {Url}", config.Url);
            return false;
        }
    }

    public async Task<MediaServerUpdateResult> UpdateMedia(IApiCredentials config, string mediaPath)
    {
        if (string.IsNullOrWhiteSpace(config.Url) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            _logger.LogWarning("No valid Plex url/apikey was found.");
            return MediaServerUpdateResult.Failed;
        }

        var normalizedMediaPath = NormalizePath(mediaPath);
        var mediaDirectory = GetParentDirectory(normalizedMediaPath);

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);

            var sections = await GetLibrarySections(client, config);
            foreach (var section in sections)
            {
                var matchingLocation = section.Locations
                    .FirstOrDefault(loc => IsPathWithin(normalizedMediaPath, NormalizePath(loc.Path)));

                if (matchingLocation == null)
                {
                    continue;
                }

                // Targeted scan: refresh only the directory containing the file
                if (!string.IsNullOrEmpty(mediaDirectory) &&
                    await ScanLibrarySection(client, config, section.Key, mediaDirectory))
                {
                    return MediaServerUpdateResult.LibraryScan;
                }

                // Fallback: refresh the entire section
                if (await ScanLibrarySection(client, config, section.Key, null))
                {
                    return MediaServerUpdateResult.LibraryScan;
                }
            }
        }
        catch (UriFormatException ex)
        {
            _logger.LogWarning(ex, "Invalid Plex URL configured: {Url}", config.Url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to update Plex media for {Path}", mediaPath);
        }

        return MediaServerUpdateResult.Failed;
    }

    private async Task<List<PlexLibrarySection>> GetLibrarySections(HttpClient client, IApiCredentials config)
    {
        using var request = CreateRequest(config, HttpMethod.Get, LibrarySectionsUrl);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("GET {Url} returned {StatusCode}: {Body}",
                request.RequestUri, response.StatusCode, responseBody);
            return [];
        }

        var container = await response.Content.ReadFromJsonAsync<PlexMediaContainer>();
        return container?.MediaContainer?.Directory ?? [];
    }

    private async Task<bool> ScanLibrarySection(HttpClient client, IApiCredentials config,
        string sectionKey, string? path)
    {
        var url = $"{LibrarySectionsUrl}/{Uri.EscapeDataString(sectionKey)}/refresh";
        if (!string.IsNullOrEmpty(path))
        {
            url += $"?path={Uri.EscapeDataString(path)}";
        }

        using var request = CreateRequest(config, HttpMethod.Get, url);
        using var response = await client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("GET {Url} returned {StatusCode}: {Body}",
            request.RequestUri, response.StatusCode, responseBody);
        return false;
    }

    private static HttpRequestMessage CreateRequest(IApiCredentials config, HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method, BuildUrl(config, relativeUrl));
        request.Headers.TryAddWithoutValidation("X-Plex-Token", config.ApiKey.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

    private static Uri BuildUrl(IApiCredentials config, string relativeUrl)
    {
        var sanitizedBaseUrl = $"{config.Url.Trim().TrimEnd('/')}/";
        if (!Uri.TryCreate(sanitizedBaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new UriFormatException($"Invalid media server URL: '{config.Url}'.");
        }

        if (!Uri.TryCreate(baseUri, relativeUrl.TrimStart('/'), out var requestUri))
        {
            throw new UriFormatException(
                $"Invalid request URL. Base URL: '{config.Url}', relative URL: '{relativeUrl}'.");
        }

        return requestUri;
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

    private static string? GetParentDirectory(string normalizedPath)
    {
        var lastSlash = normalizedPath.LastIndexOf('/');
        return lastSlash > 0 ? normalizedPath[..lastSlash] : null;
    }

    // Plex JSON response models
    private sealed class PlexMediaContainer
    {
        [JsonPropertyName("MediaContainer")] public PlexMediaContainerContent? MediaContainer { get; init; }
    }

    private sealed class PlexMediaContainerContent
    {
        [JsonPropertyName("Directory")] public List<PlexLibrarySection> Directory { get; init; } = [];
    }

    private sealed class PlexLibrarySection
    {
        [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;
        [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
        [JsonPropertyName("Location")] public List<PlexLocation> Locations { get; init; } = [];
    }

    private sealed class PlexLocation
    {
        [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;
    }
}
