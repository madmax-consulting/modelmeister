namespace ModelMeister.Inriver.ServerSettings;

/// <summary>Pure dictionary diff used to compare two server-settings captures (or any string→string maps).</summary>
public static class ServerSettingsDiff
{
    /// <summary>
    /// Compute the per-key delta between <paramref name="left"/> and <paramref name="right"/>.
    /// Comparisons are ordinal on both keys and values.
    /// </summary>
    public static ServerSettingsDelta Compute(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        var allKeys = new HashSet<string>(left.Keys, StringComparer.Ordinal);
        allKeys.UnionWith(right.Keys);

        var onlyLeft = new List<string>();
        var onlyRight = new List<string>();
        var changed = new List<ServerSettingChange>();
        var unchanged = 0;

        foreach (var k in allKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var inL = left.TryGetValue(k, out var lv);
            var inR = right.TryGetValue(k, out var rv);
            if (inL && !inR) onlyLeft.Add(k);
            else if (!inL && inR) onlyRight.Add(k);
            else if (!string.Equals(lv, rv, StringComparison.Ordinal)) changed.Add(new ServerSettingChange(k, lv!, rv!));
            else unchanged++;
        }

        return new ServerSettingsDelta(onlyLeft, onlyRight, changed, unchanged);
    }
}

/// <summary>Result of a <see cref="ServerSettingsDiff.Compute"/> call.</summary>
public sealed record ServerSettingsDelta(
    IReadOnlyList<string> OnlyInLeft,
    IReadOnlyList<string> OnlyInRight,
    IReadOnlyList<ServerSettingChange> Changed,
    int UnchangedCount)
{
    public int TotalDifferences => OnlyInLeft.Count + OnlyInRight.Count + Changed.Count;
}

/// <summary>One per-key value difference between two captures.</summary>
public sealed record ServerSettingChange(string Key, string LeftValue, string RightValue);
