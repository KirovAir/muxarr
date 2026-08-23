using Muxarr.Core.Models;
using Muxarr.Data.Entities;

namespace Muxarr.Web.Helpers;

// The words for the subtitle type policy, shared by the editor and the overview
// so a choice reads the same everywhere it shows up.
public static class PolicyText
{
    public static string Coverage(SubtitleCoverage coverage)
    {
        return coverage switch
        {
            SubtitleCoverage.PreferFull => "Prefer full subtitles",
            SubtitleCoverage.FullOnly => "Full subtitles only",
            SubtitleCoverage.ForcedOnly => "Forced subtitles only",
            _ => "Keep both"
        };
    }

    public static string CoverageHelp(SubtitleCoverage coverage)
    {
        return coverage switch
        {
            SubtitleCoverage.PreferFull => "Forced subtitles are kept only when the language has no full subtitle.",
            SubtitleCoverage.FullOnly => "Forced subtitles are removed, even when they are all the language has.",
            SubtitleCoverage.ForcedOnly => "Full subtitles are removed, even when they are all the language has.",
            _ => "Full and forced subtitles are both kept."
        };
    }

    // "SDH subtitles" or "Audio description", and what counts as better than it.
    public static string ImpairedName(MediaTrackType type)
    {
        return type == MediaTrackType.Audio ? "Audio description" : "SDH subtitles";
    }

    public static string Extra(ExtraPolicy policy, string better)
    {
        return policy switch
        {
            ExtraPolicy.KeepWhenAlone => $"Keep only when there is no {better}",
            ExtraPolicy.Remove => "Remove",
            _ => "Keep"
        };
    }

    public static string ImpairedBetter(MediaTrackType type)
    {
        return type == MediaTrackType.Audio ? "other audio track" : "full subtitle";
    }

    public static string CommentaryName(MediaTrackType type)
    {
        return type == MediaTrackType.Audio ? "Commentary" : "Commentary subtitles";
    }

    public static string CommentaryBetter(MediaTrackType type)
    {
        return type == MediaTrackType.Audio ? "other audio track" : "other subtitle";
    }

    // One line for the profile: "Full and forced. SDH and commentary only when needed."
    // With onlyChanges the defaults are left out, for the overview.
    public static string Summary(TrackSettings s, MediaTrackType type, bool onlyChanges = false)
    {
        var parts = new List<string>();
        if (type == MediaTrackType.Subtitles && !(onlyChanges && s.Coverage == SubtitleCoverage.Both))
        {
            parts.Add(s.Coverage switch
            {
                SubtitleCoverage.PreferFull => "Full subtitles, forced only when needed",
                SubtitleCoverage.FullOnly => "Full subtitles only",
                SubtitleCoverage.ForcedOnly => "Forced subtitles only",
                _ => "Full and forced"
            });
        }

        var sdh = type == MediaTrackType.Audio ? "audio description" : "SDH";
        var extras = new List<(string Name, ExtraPolicy Policy)> { (sdh, s.Impaired), ("commentary", s.Commentary) };
        foreach (var policy in new[] { ExtraPolicy.Remove, ExtraPolicy.KeepWhenAlone, ExtraPolicy.Keep })
        {
            var names = extras.Where(e => e.Policy == policy).Select(e => e.Name).ToList();
            if (names.Count == 0 || (onlyChanges && policy == ExtraPolicy.Keep))
            {
                continue;
            }

            var joined = string.Join(policy == ExtraPolicy.Remove ? " or " : " and ", names);
            parts.Add(policy switch
            {
                ExtraPolicy.Remove => $"No {joined}",
                ExtraPolicy.KeepWhenAlone => $"{Capitalize(joined)} only when needed",
                _ => $"{Capitalize(joined)} kept"
            });
        }

        return string.Join(". ", parts) + ".";
    }

    // The short note on a language row: only what differs from the profile.
    public static string Overrides(LanguagePreference pref, MediaTrackType type)
    {
        var parts = new List<string>();
        if (pref.Coverage is { } coverage && type == MediaTrackType.Subtitles)
        {
            parts.Add(Coverage(coverage));
        }

        if (pref.Impaired is { } impaired)
        {
            var name = type == MediaTrackType.Audio ? "audio description" : "SDH";
            parts.Add(impaired switch
            {
                ExtraPolicy.Remove => $"No {name}",
                ExtraPolicy.KeepWhenAlone => $"{Capitalize(name)} only when needed",
                _ => $"{Capitalize(name)} kept"
            });
        }

        if (pref.Commentary is { } commentary)
        {
            parts.Add(commentary switch
            {
                ExtraPolicy.Remove => "No commentary",
                ExtraPolicy.KeepWhenAlone => "Commentary only when needed",
                _ => "Commentary kept"
            });
        }

        if (pref.MaxTracks is { } max)
        {
            parts.Add(type == MediaTrackType.Subtitles ? $"{max} of each" : $"Best {max}");
        }

        return string.Join(" · ", parts);
    }

    private static string Capitalize(string text)
    {
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
