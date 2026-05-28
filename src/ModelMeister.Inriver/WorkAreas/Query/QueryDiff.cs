namespace ModelMeister.Inriver.WorkAreas.Query;

/// <summary>
/// Field-level, human-readable diff between two serialized saved searches. Used by the work-area Compare to
/// replace a flat "saved query differs" with the actual changes ("+ data: Price &gt; 10", "Channel: — → 42").
/// Returns an empty list when the two queries are equivalent for the parts the model captures.
/// </summary>
public static class QueryDiff
{
    public static IReadOnlyList<string> Describe(string? leftJson, string? rightJson)
    {
        var left = QueryMapper.ToModel(WorkAreaService.DeserializeQuery(leftJson));
        var right = QueryMapper.ToModel(WorkAreaService.DeserializeQuery(rightJson));
        var lines = new List<string>();

        if (!string.Equals(left.EntityTypeId, right.EntityTypeId, StringComparison.OrdinalIgnoreCase))
            lines.Add($"Entity type: {Show(left.EntityTypeId)} → {Show(right.EntityTypeId)}");
        if (left.ChannelId != right.ChannelId)
            lines.Add($"Channel: {Show(left.ChannelId)} → {Show(right.ChannelId)}");

        DiffGroup(left.DataQuery, right.DataQuery, "data", lines);
        DiffKeys(left.SystemCriteria.Select(SysKey).ToList(), right.SystemCriteria.Select(SysKey).ToList(), "system", lines);
        DiffLink(left.LinkQuery, right.LinkQuery, lines);
        return lines;
    }

    private static void DiffGroup(CriteriaGroup? l, CriteriaGroup? r, string label, List<string> lines)
    {
        if (l?.Join != r?.Join && (l is not null || r is not null))
            lines.Add($"{label} join: {Show(l?.Join.ToString())} → {Show(r?.Join.ToString())}");
        DiffKeys(Flatten(l), Flatten(r), label, lines);
    }

    private static void DiffLink(LinkQueryModel? l, LinkQueryModel? r, List<string> lines)
    {
        if (l is null && r is null) return;
        if (l is null) { lines.Add("+ link query added"); }
        else if (r is null) { lines.Add("− link query removed"); }
        else
        {
            if (!string.Equals(l.LinkTypeId, r.LinkTypeId, StringComparison.OrdinalIgnoreCase))
                lines.Add($"link type: {Show(l.LinkTypeId)} → {Show(r.LinkTypeId)}");
            if (l.Direction != r.Direction)
                lines.Add($"link direction: {l.Direction} → {r.Direction}");
            if (!string.Equals(l.SourceEntityTypeId, r.SourceEntityTypeId, StringComparison.OrdinalIgnoreCase))
                lines.Add($"link source type: {Show(l.SourceEntityTypeId)} → {Show(r.SourceEntityTypeId)}");
            if (!string.Equals(l.TargetEntityTypeId, r.TargetEntityTypeId, StringComparison.OrdinalIgnoreCase))
                lines.Add($"link target type: {Show(l.TargetEntityTypeId)} → {Show(r.TargetEntityTypeId)}");
            DiffKeys(l.SourceCriteria.Select(CritKey).ToList(), r.SourceCriteria.Select(CritKey).ToList(), "link source", lines);
            DiffKeys(l.TargetCriteria.Select(CritKey).ToList(), r.TargetCriteria.Select(CritKey).ToList(), "link target", lines);
        }
    }

    private static void DiffKeys(List<string> left, List<string> right, string label, List<string> lines)
    {
        foreach (var k in right.Except(left, StringComparer.Ordinal)) lines.Add($"+ {label}: {k}");
        foreach (var k in left.Except(right, StringComparer.Ordinal)) lines.Add($"− {label}: {k}");
    }

    private static List<string> Flatten(CriteriaGroup? g)
    {
        var keys = new List<string>();
        while (g is not null)
        {
            keys.AddRange(g.Criteria.Select(CritKey));
            g = g.SubQuery;
        }
        return keys;
    }

    private static string CritKey(CriterionModel c) => $"{c.FieldTypeId} {c.Operator} {c.Value}".Trim();
    private static string SysKey(SystemFieldCriterion c) => $"{c.Field} {c.Operator} {c.Value}".Trim();
    private static string Show(string? s) => string.IsNullOrEmpty(s) ? "—" : s;
    private static string Show(int? i) => i?.ToString() ?? "—";
}
