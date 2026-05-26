namespace ModelMeister.Model.Completeness;

/// <summary>
/// The built-in completeness rule kinds the DSL can express — one per
/// <see cref="CompletenessRuleAttribute"/> subclass.
/// </summary>
public enum CompletenessRuleKind
{
    FieldNotEmpty,
    ContainsValue,
    ExactMatch,
    LinkTypeExists,
    RelationsComplete,
    NumberEvaluation,
}

/// <summary>
/// Single source of truth for the round-trip between the strongly-typed completeness DSL and inriver's
/// loosely-typed completeness model. inriver stores each rule as a <c>CompletenessBusinessRule</c> with a
/// string <c>Type</c> plus a bag of <c>RuleSettings</c> (Key/Value). This table maps each
/// <see cref="CompletenessRuleKind"/> ↔ that inriver <c>Type</c> string and names the setting keys.
/// </summary>
/// <remarks>
/// IMPORTANT: the <c>Type</c> strings and setting keys below are inriver-server vocabulary. They are
/// consumed by the scaffolder (Type → attribute), the mapper (attribute → Type) and the differ, so the
/// scaffold→load→map→diff round-trip is self-consistent regardless of the exact spelling. The ONE place
/// that must match a real environment is this table — confirm against a live env via
/// <c>ModelService.GetAllCompletenessCriteras()</c> or an <c>env snapshot</c> that has completeness
/// configured, and correct the constants here if they differ. <c>CompletenessRuleVocabularyTests</c>
/// pins the current values so any change is intentional.
/// </remarks>
public static class CompletenessRuleVocabulary
{
    // ---- Setting keys (Key on a CompletenessRuleSetting) ----

    /// <summary>Setting key whose value is the field-type id the rule is attached to. Present on every kind.</summary>
    public const string FieldTypeIdKey = "FieldTypeId";

    /// <summary>Setting key carrying the comparison value for ContainsValue / ExactMatch.</summary>
    public const string ValueKey = "Value";

    /// <summary>Setting key carrying the link-type id for LinkTypeExists.</summary>
    public const string LinkTypeIdKey = "LinkTypeId";

    /// <summary>Setting key carrying the comparison operator for NumberEvaluation.</summary>
    public const string OperatorKey = "Operator";

    /// <summary>The Key/Value <c>Type</c> stamped on a setting (inriver echoes the rule type here).</summary>
    public const string SettingType = "String";

    private static readonly IReadOnlyDictionary<CompletenessRuleKind, string> KindToType =
        new Dictionary<CompletenessRuleKind, string>
        {
            [CompletenessRuleKind.FieldNotEmpty] = "FieldNotEmpty",
            [CompletenessRuleKind.ContainsValue] = "FieldContainsValue",
            [CompletenessRuleKind.ExactMatch] = "FieldExactValue",
            [CompletenessRuleKind.LinkTypeExists] = "LinkTypeExists",
            [CompletenessRuleKind.RelationsComplete] = "RelationsComplete",
            [CompletenessRuleKind.NumberEvaluation] = "FieldNumberEvaluation",
        };

    private static readonly IReadOnlyDictionary<string, CompletenessRuleKind> TypeToKind =
        KindToType.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The inriver <c>CompletenessBusinessRule.Type</c> string for a DSL rule kind.</summary>
    public static string InriverType(CompletenessRuleKind kind) =>
        KindToType.TryGetValue(kind, out var t)
            ? t
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "No inriver Type mapping for completeness rule kind.");

    /// <summary>Resolve an inriver <c>CompletenessBusinessRule.Type</c> string back to a DSL rule kind.</summary>
    public static bool TryKind(string inriverType, out CompletenessRuleKind kind) =>
        TypeToKind.TryGetValue(inriverType ?? string.Empty, out kind);
}
