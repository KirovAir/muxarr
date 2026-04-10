using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Muxarr.Core.Api.Models;
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
        return await Send(config, HttpMethod.Get, IdentityUrl);
    }

    public async Task<MediaServerUpdateResult> UpdateMedia(IApiCredentials config, string mediaPath)
    {
        var normalizedMediaPath = NormalizePath(mediaPath);
        var mediaDirectory = GetParentDirectory(normalizedMediaPath);

        var container = await Get<PlexMediaContainer>(config, LibrarySectionsUrl);
        var sections = container?.MediaContainer?.Directory ?? [];

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
                await ScanLibrarySection(config, section.Key, mediaDirectory))
            {
                return MediaServerUpdateResult.LibraryScan;
            }

            // Fallback: refresh the entire section
            if (await ScanLibrarySection(config, section.Key, null))
            {
                return MediaServerUpdateResult.LibraryScan;
            }
        }

        return MediaServerUpdateResult.Failed;
    }

    private async Task<bool> ScanLibrarySection(IApiCredentials config, string sectionKey, string? path)
    {
        var url = $"{LibrarySectionsUrl}/{Uri.EscapeDataString(sectionKey)}/refresh";
        if (!string.IsNullOrEmpty(path))
        {
            url += $"?path={Uri.EscapeDataString(path)}";
        }

        return await Send(config, HttpMethod.Get, url);
    }

    private async Task<T?> Get<T>(IApiCredentials config, string relativeUrl) where T : class
    {
        if (string.IsNullOrWhiteSpace(config.Url) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            _logger.LogWarning("No valid {ParentClass} url/apikey was found.", GetType().Name);
            return null;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = CreateRequest(config, HttpMethod.Get, relativeUrl);
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

    private async Task<bool> Send(IApiCredentials config, HttpMethod method, string relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(config.Url) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            _logger.LogWarning("No valid {ParentClass} url/apikey was found.", GetType().Name);
            return false;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = CreateRequest(config, method, relativeUrl);
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

}
