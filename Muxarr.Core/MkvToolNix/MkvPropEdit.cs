using Muxarr.Core.Models;
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
    public static async Task<ProcessResult> EditTrackProperties(string file, List<TrackOutput> tracks)
    {
        var command = $"\"{file}\"";

        foreach (var track in tracks)
        {
            // mkvpropedit uses 1-based track numbers
            var selector = $"--edit track:{track.TrackNumber + 1}";
            var props = "";

            if (track.Name != null)
            {
                props += $" --set name={MkvToolNixHelper.EscapeValue(track.Name)}";
            }

            if (track.LanguageCode != null)
            {
                props += $" --set language={track.LanguageCode}";
            }

            if (track.IsDefault != null)
            {
                props += $" --set flag-default={(track.IsDefault.Value ? "1" : "0")}";
            }

            if (track.IsForced != null)
            {
                props += $" --set flag-forced={(track.IsForced.Value ? "1" : "0")}";
            }

            if (track.IsHearingImpaired != null)
            {
                props += $" --set flag-hearing-impaired={(track.IsHearingImpaired.Value ? "1" : "0")}";
            }

            if (track.IsCommentary != null)
            {
                props += $" --set flag-commentary={(track.IsCommentary.Value ? "1" : "0")}";
            }

            if (!string.IsNullOrEmpty(props))
            {
                command += $" {selector}{props}";
            }
        }

        return await ProcessExecutor.ExecuteProcessAsync(Executable, command, TimeSpan.FromMinutes(5));
    }
}
