using Muxarr.Core.Extensions;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

public static class ProfileExtensions
{
    public static Profile? GetBestCandidate(this IEnumerable<Profile> list, string path)
    {
        return list.FirstOrDefault(x =>
            x.Directories.Any(y => path.StartsWith(y, StringComparison.InvariantCultureIgnoreCase)));
    }

    /// <summary>
    /// The languages an entry is a fallback for: the ones above it in its group,
    /// in priority order. Empty when the entry is kept unconditionally.
    /// </summary>
    public static List<LanguagePreference> FallbackFor(this List<LanguagePreference> languages, int index)
    {
        var covered = new List<LanguagePreference>();
        if (!languages[index].IsFallback)
        {
            return covered;
        }

        for (var i = index - 1; i >= 0; i--)
        {
            covered.Insert(0, languages[i]);
            if (!languages[i].IsFallback)
            {
                break;
            }
        }

        return covered;
    }

    public static Profile Clone(this Profile profile)
    {
        var clone = new Profile();
        clone.AudioSettings = profile.AudioSettings.LazyClone();
        clone.SubtitleSettings = profile.SubtitleSettings.LazyClone();
        clone.Directories = profile.Directories.LazyClone();
        clone.Name = profile.Name;
        clone.Id = profile.Id;
        clone.ClearVideoTrackNames = profile.ClearVideoTrackNames;
        clone.ClearFileTitle = profile.ClearFileTitle;
        clone.RemoveChapters = profile.RemoveChapters;
        clone.TrimToVideoLength = profile.TrimToVideoLength;
        clone.SkipHardlinkedFiles = profile.SkipHardlinkedFiles;
        return clone;
    }
}
