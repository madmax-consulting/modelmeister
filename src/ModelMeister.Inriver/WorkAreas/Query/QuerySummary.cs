using System.Text;

namespace ModelMeister.Inriver.WorkAreas.Query;

/// <summary>
/// Renders a <see cref="QueryModel"/> as a one-paragraph, human-readable description ("Product where
/// (ProductName contains 'widget' AND Price &gt; 10); linked OutBound ProductItem to Item"). Pure and
/// env-free so the builder can recompute it live on every edit and tests can assert it without a connection.
/// </summary>
public static class QuerySummary
{
    public static string Describe(QueryModel model)
    {
        var parts = new List<string>();

        var scope = string.IsNullOrWhiteSpace(model.EntityTypeId) ? "Any entity" : model.EntityTypeId!;
        if (model.ChannelId is { } ch) scope += $" in channel {ch}";

        var data = DescribeGroup(model.DataQuery);
        parts.Add(data.Length == 0 ? scope : $"{scope} where {data}");

        if (model.SystemCriteria.Count > 0)
            parts.Add("system: " + string.Join(", ", model.SystemCriteria.Select(s =>
                $"{s.Field} {Op(s.Operator)} {Val(s.Value)}".Trim())));

        if (model.LinkQuery is { } lq)
        {
            var sb = new StringBuilder("linked ");
            sb.Append(lq.Direction);
            if (!string.IsNullOrWhiteSpace(lq.LinkTypeId)) sb.Append(' ').Append(lq.LinkTypeId);
            if (!string.IsNullOrWhiteSpace(lq.TargetEntityTypeId)) sb.Append(" to ").Append(lq.TargetEntityTypeId);
            var src = DescribeFlat(lq.SourceCriteria, "AND");
            var tgt = DescribeFlat(lq.TargetCriteria, "AND");
            if (src.Length > 0) sb.Append(" [source: ").Append(src).Append(']');
            if (tgt.Length > 0) sb.Append(" [target: ").Append(tgt).Append(']');
            parts.Add(sb.ToString());
        }

        if (model.HasUnsupportedParts)
            parts.Add("(plus completeness/specification criteria preserved unchanged)");

        return string.Join("; ", parts);
    }

    private static string DescribeGroup(CriteriaGroup? g)
    {
        if (g is null) return "";
        var join = g.Join == QJoin.Or ? "OR" : "AND";
        var here = DescribeFlat(g.Criteria, join);
        var sub = DescribeGroup(g.SubQuery);
        if (here.Length == 0) return sub;
        if (sub.Length == 0) return here;
        return $"{here} {join} ({sub})";
    }

    private static string DescribeFlat(IReadOnlyList<CriterionModel> criteria, string join)
    {
        var rendered = criteria
            .Where(c => !string.IsNullOrWhiteSpace(c.FieldTypeId))
            .Select(c => $"{c.FieldTypeId} {Op(c.Operator)} {Val(c.Value)}".Trim())
            .ToList();
        return rendered.Count switch
        {
            0 => "",
            1 => rendered[0],
            _ => "(" + string.Join($" {join} ", rendered) + ")",
        };
    }

    private static string Op(QOperator op) => op switch
    {
        QOperator.Equal => "=",
        QOperator.NotEqual => "≠",
        QOperator.GreaterThan => ">",
        QOperator.GreaterThanOrEqual => "≥",
        QOperator.LessThan => "<",
        QOperator.LessThanOrEqual => "≤",
        QOperator.Contains => "contains",
        QOperator.NotContains => "does not contain",
        QOperator.BeginsWith => "begins with",
        QOperator.IsNull => "is null",
        QOperator.IsNotNull => "is not null",
        QOperator.IsTrue => "is true",
        QOperator.IsFalse => "is false",
        QOperator.Empty => "is empty",
        QOperator.NotEmpty => "is not empty",
        QOperator.ContainsAll => "contains all",
        QOperator.ContainsAny => "contains any",
        QOperator.NotContainsAny => "contains none of",
        QOperator.NotContainsAll => "does not contain all",
        _ => op.ToString(),
    };

    // Value-less operators carry no value; keep their rendering clean.
    private static string Val(string? value) => string.IsNullOrEmpty(value) ? "" : $"'{value}'";
}
