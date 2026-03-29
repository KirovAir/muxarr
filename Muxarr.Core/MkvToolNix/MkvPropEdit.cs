using Muxarr.Core.Utilities;

namespace Muxarr.Core.MkvToolNix;

public static class MkvPropEdit
{
    private const string Executable = "mkvpropedit";

    /// <summary>
    /// Edits track properties (name, language) in-place without remuxing.
    /// Track IDs use mkvmerge's 0-based numbering; mkvpropedit uses 1-based,
    /// so we add 1 to each track ID.
    /// </summary>
    public static async Task<ProcessResult> EditTrackProperties(string file,
        Dictionary<int, TrackMetadata> trackMetadata)
    {
        var command = $"\"{file}\"";

        foreach (var (trackId, metadata) in trackMetadata)
        {
            // mkvpropedit uses 1-based track numbers
            var selector = $"--edit track:{trackId + 1}";
            var props = "";

            if (metadata.Name != null)
            {
                props += $" --set name={EscapeValue(metadata.Name)}";
            }

            if (metadata.LanguageCode != null)
            {
                props += $" --set language={metadata.LanguageCode}";
            }

            if (!string.IsNullOrEmpty(props))
            {
                command += $" {selector}{props}";
            }
        }

        return await ProcessExecutor.ExecuteProcessAsync(Executable, command, TimeSpan.FromMinutes(5));
    }

    private static string EscapeValue(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}
