namespace ModelMeister.Inriver.Extensions;

/// <summary>
/// Pure function: compute the per-extension delta between two environment captures. Used by the
/// cross-env extensions compare page.
/// </summary>
public static class ExtensionsDiff
{
    /// <summary>Diff two ExtensionInfo lists keyed by <see cref="ExtensionsService.ExtensionInfo.Id"/>.</summary>
    public static ExtensionsDelta Compute(
        IReadOnlyList<ExtensionsService.ExtensionInfo> left,
        IReadOnlyList<ExtensionsService.ExtensionInfo> right)
    {
        var leftMap = left.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var rightMap = right.ToDictionary(e => e.Id, StringComparer.Ordinal);

        var onlyLeft = new List<string>();
        var onlyRight = new List<string>();
        var changed = new List<ExtensionChange>();
        var unchanged = 0;

        var allIds = new HashSet<string>(leftMap.Keys, StringComparer.Ordinal);
        allIds.UnionWith(rightMap.Keys);

        foreach (var id in allIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            var inL = leftMap.TryGetValue(id, out var l);
            var inR = rightMap.TryGetValue(id, out var r);

            if (inL && !inR) { onlyLeft.Add(id); continue; }
            if (!inL && inR) { onlyRight.Add(id); continue; }

            var change = CompareOne(l!, r!);
            if (change is null) unchanged++;
            else changed.Add(change);
        }

        return new ExtensionsDelta(onlyLeft, onlyRight, changed, unchanged);
    }

    private static ExtensionChange? CompareOne(ExtensionsService.ExtensionInfo l, ExtensionsService.ExtensionInfo r)
    {
        var differences = new List<string>();
        if (!string.Equals(l.TypeName ?? "", r.TypeName ?? "", StringComparison.Ordinal))
            differences.Add($"TypeName: left='{l.TypeName}' right='{r.TypeName}'");
        if (l.IsStarted != r.IsStarted)
            differences.Add($"IsStarted: left={l.IsStarted} right={r.IsStarted}");

        var settingsDelta = ServerSettings.ServerSettingsDiff.Compute(l.Settings, r.Settings);
        if (differences.Count == 0 && settingsDelta.TotalDifferences == 0) return null;

        return new ExtensionChange(l.Id, differences, settingsDelta);
    }
}

/// <summary>Result of an extensions cross-env diff.</summary>
public sealed record ExtensionsDelta(
    IReadOnlyList<string> OnlyInLeft,
    IReadOnlyList<string> OnlyInRight,
    IReadOnlyList<ExtensionChange> Changed,
    int UnchangedCount)
{
    public int TotalDifferences => OnlyInLeft.Count + OnlyInRight.Count + Changed.Count;
}

/// <summary>Per-extension difference: high-level field deltas plus a per-setting dictionary diff.</summary>
public sealed record ExtensionChange(
    string Id,
    IReadOnlyList<string> Differences,
    ServerSettings.ServerSettingsDelta Settings);
