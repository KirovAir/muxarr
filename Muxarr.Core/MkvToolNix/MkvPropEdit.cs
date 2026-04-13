using Muxarr.Core.Models;
using Muxarr.Core.Utilities;

namespace Muxarr.Core.MkvToolNix;

// In-place metadata editor for Matroska. No container rewrite - orders of
// magnitude faster than remuxing when only flags/names/languages change.
// Uses the unified converter signature but edits input directly; output is
// ignored (kept for signature parity with MkvMerge/FFmpeg).
public static class MkvPropEdit
{
    private const string Executable = "mkvpropedit";

    public static bool IsSuccess(ProcessResult result)
    {
        return result.ExitCode == 0;
    }

    public static async Task<ProcessResult> Apply(string input, string output, ConversionPlan plan,
        Action<string, int>? onProgress = null, TimeSpan? timeout = null)
    {
        _ = output;

        var command = $"\"{input}\"";
        var editCount = 0;

        foreach (var track in plan.Delta.Tracks)
        {
            // mkvpropedit uses 1-based track numbers; mkvmerge/our model are 0-based.
            var selector = $"--edit track:{track.TrackNumber + 1}";
            var props = "";

            if (track.Name != null)
            {
                props += track.Name.Length == 0
                    ? " --delete name"
                    : $" --set name={MkvToolNixHelper.EscapeValue(track.Name)}";
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

            if (track.IsVisualImpaired != null)
            {
                props += $" --set flag-visual-impaired={(track.IsVisualImpaired.Value ? "1" : "0")}";
            }

            if (track.IsCommentary != null)
            {
                props += $" --set flag-commentary={(track.IsCommentary.Value ? "1" : "0")}";
            }

            if (track.IsOriginal != null)
            {
                props += $" --set flag-original={(track.IsOriginal.Value ? "1" : "0")}";
            }

            if (!string.IsNullOrEmpty(props))
            {
                command += $" {selector}{props}";
                editCount++;
            }
        }

        // Guard against plan/apply drift: if a strategy of MetadataEdit
        // produced a delta whose fields Apply can't express (e.g. a bare
        // IsDub change that the planner should have folded into a title
        // rewrite), running mkvpropedit with no --edit args would fail with
        // "Nothing to do". Treat that as a no-op success; the dispatcher's
        // post-edit rescan will catch a genuine miss and fall through to remux.
        if (editCount == 0)
        {
            onProgress?.Invoke("mkvpropedit: no edits to apply", 100);
            return new ProcessResult { ExitCode = 0 };
        }

        onProgress?.Invoke("mkvpropedit: starting", 0);
        var result = await ProcessExecutor.ExecuteProcessAsync(Executable, command, timeout ?? TimeSpan.FromMinutes(5));
        onProgress?.Invoke("mkvpropedit: done", 100);
        return result;
    }
}
