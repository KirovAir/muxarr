using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

public static class ProfileExtensions
{
    public static Profile? GetBestCandidate(this IEnumerable<Profile> list, string path)
    {
        return list.FirstOrDefault(x =>
            x.Directories.Any(y => path.StartsWith(y, StringComparison.InvariantCultureIgnoreCase)));
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
        return clone;
    }
}