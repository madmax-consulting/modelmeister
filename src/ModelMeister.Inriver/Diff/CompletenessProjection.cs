using System.Globalization;
using System.Text;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Completeness;
using ModelMeister.Model.Loading;

namespace ModelMeister.Inriver.Diff;

/// <summary>
/// Canonicalises a completeness definition (from the code model or a live snapshot) into a stable string
/// so the differ can compare the two without depending on inriver-assigned numeric ids. Groups are keyed
/// by display name, rules by (field, kind, args); inriver's loosely-typed rules are normalised back to the
/// DSL shape via <see cref="CompletenessRuleVocabulary"/>. Sorting makes member ordering irrelevant.
/// </summary>
internal static class CompletenessProjection
{
    public static string FromLoaded(LoadedCompletenessDefinition def) =>
        Canonicalize(def.EntityTypeId, def.Groups.Select(g =>
            (g.Name.DefaultValue, g.Weight, g.SortOrder, g.Rules.Select(LoadedRuleLine))));

    public static string FromLive(LiveCompletenessDefinition def) =>
        Canonicalize(def.EntityTypeId, def.Groups.Select(g =>
            (g.Name.DefaultValue, g.Weight, g.SortOrder, g.Rules.Select(LiveRuleLine))));

    private static string LoadedRuleLine(LoadedCompletenessRule r)
    {
        (string? value, string? link, string? op, string? num) = r.Kind switch
        {
            CompletenessRuleKind.ContainsValue or CompletenessRuleKind.ExactMatch => (r.Value, (string?)null, (string?)null, (string?)null),
            CompletenessRuleKind.LinkTypeExists => ((string?)null, r.LinkTypeId, (string?)null, (string?)null),
            CompletenessRuleKind.NumberEvaluation => ((string?)null, (string?)null, r.Operator?.ToString(), Num(r.Number)),
            _ => ((string?)null, (string?)null, (string?)null, (string?)null),
        };
        return Rule(r.FieldId, r.Kind.ToString(), r.Weight, value, link, op, num);
    }

    private static string LiveRuleLine(LiveCompletenessBusinessRule r)
    {
        string? Setting(string key) => r.Settings
            .FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

        // An unknown rule type can't be reproduced by the code model — render it raw so it always differs.
        if (!CompletenessRuleVocabulary.TryKind(r.Type, out var kind))
        {
            var settings = string.Join(',', r.Settings
                .OrderBy(s => s.Key, StringComparer.Ordinal)
                .Select(s => $"{s.Key}={s.Value}"));
            return Rule(Setting(CompletenessRuleVocabulary.FieldTypeIdKey) ?? "", "RAW:" + r.Type, r.Weight, settings, null, null, null);
        }

        var fieldId = Setting(CompletenessRuleVocabulary.FieldTypeIdKey) ?? string.Empty;
        (string? value, string? link, string? op, string? num) = kind switch
        {
            CompletenessRuleKind.ContainsValue or CompletenessRuleKind.ExactMatch
                => (Setting(CompletenessRuleVocabulary.ValueKey), (string?)null, (string?)null, (string?)null),
            CompletenessRuleKind.LinkTypeExists
                => ((string?)null, Setting(CompletenessRuleVocabulary.LinkTypeIdKey), (string?)null, (string?)null),
            CompletenessRuleKind.NumberEvaluation
                => ((string?)null, (string?)null, Setting(CompletenessRuleVocabulary.OperatorKey), Num(Setting(CompletenessRuleVocabulary.ValueKey))),
            _ => ((string?)null, (string?)null, (string?)null, (string?)null),
        };
        return Rule(fieldId, kind.ToString(), r.Weight, value, link, op, num);
    }

    private static string Canonicalize(
        string entityTypeId,
        IEnumerable<(string Key, int Weight, int SortOrder, IEnumerable<string> Rules)> groups)
    {
        var sb = new StringBuilder();
        sb.Append("ET=").Append(entityTypeId).Append('\n');
        foreach (var g in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.Append("G=").Append(g.Key).Append('|').Append(g.Weight).Append('|').Append(g.SortOrder).Append('\n');
            foreach (var r in g.Rules.OrderBy(x => x, StringComparer.Ordinal))
                sb.Append("  R=").Append(r).Append('\n');
        }
        return sb.ToString();
    }

    private static string Rule(string fieldId, string kind, int weight, string? value, string? link, string? op, string? num) =>
        string.Join('|', fieldId, kind, weight.ToString(CultureInfo.InvariantCulture), value ?? "", link ?? "", op ?? "", num ?? "");

    private static string? Num(string? raw) =>
        string.IsNullOrEmpty(raw) ? raw
        : double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d.ToString("R", CultureInfo.InvariantCulture)
            : raw;

    private static string? Num(double? d) => d?.ToString("R", CultureInfo.InvariantCulture);
}
