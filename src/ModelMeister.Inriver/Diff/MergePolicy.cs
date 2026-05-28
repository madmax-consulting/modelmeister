using System;
using System.Collections.Generic;

namespace ModelMeister.Inriver.Diff;

/// <summary>How a <see cref="FieldIdIgnoreRule"/> matches a field-type id.</summary>
public enum FieldIdMatchKind
{
    Contains,
    StartsWith,
    EndsWith,
}

/// <summary>
/// Suppresses all diffs (add/update/delete) for field types whose id matches <see cref="Value"/>
/// under <see cref="Kind"/>. Matching is ordinal, case-insensitive.
/// </summary>
public sealed record FieldIdIgnoreRule(FieldIdMatchKind Kind, string Value);

/// <summary>
/// Flags controlling diff aggressiveness. Defaults are protective — apply will not delete
/// or perform destructive datatype changes unless explicitly allowed. Pass an instance to
/// <see cref="ModelDiffer.Diff"/>, or use <see cref="Default"/> for the safe baseline.
/// </summary>
public sealed record MergePolicy
{
    /// <summary>When true, code-side names and descriptions overwrite live values on update.</summary>
    public bool OverwriteNamesAndDescriptions { get; init; }

    /// <summary>When true, code-side CVL value labels and parent keys overwrite live values.</summary>
    public bool OverwriteCvlValues { get; init; }

    /// <summary>When true (the default), a field type's Index/SortOrder difference is ignored on update.</summary>
    public bool IgnoreFieldIndexSortingOnUpdate { get; init; } = true;

    /// <summary>When true (the default), a category's Index/SortOrder difference is ignored on update.</summary>
    public bool IgnoreCategoryIndexSortingOnUpdate { get; init; } = true;

    /// <summary>When true (the default), a link type's Index/SortOrder difference is ignored on update.</summary>
    public bool IgnoreLinkTypeIndexSortingOnUpdate { get; init; } = true;

    /// <summary>When true, items that exist on the live side but not in code are deleted/deactivated.</summary>
    public bool AllowDeletes { get; init; }

    /// <summary>When true, a field's <see cref="Model.Primitives.Datatype"/> change is applied; otherwise it surfaces as a warning.</summary>
    public bool AllowDatatypeChange { get; init; }

    /// <summary>Reserved — when true, a CVL value rename (key change while id is stable) is permitted.</summary>
    public bool AllowCvlValueRename { get; init; }

    /// <summary>
    /// Field-type property names (e.g. "TrackChanges", "Index", "Description") whose differences
    /// are ignored during diff. Matched case-insensitively against the tokens used in
    /// <c>ModelDiffer.FieldDiffers</c>. See <see cref="IgnoresProperty"/>.
    /// </summary>
    public IReadOnlyList<string> IgnoredFieldProperties { get; init; } = [];

    /// <summary>Patterns matching field-type ids whose differences are ignored entirely.</summary>
    public IReadOnlyList<FieldIdIgnoreRule> IgnoredFieldIdPatterns { get; init; } = [];

    /// <summary>True when <paramref name="fieldId"/> matches any configured ignore pattern.</summary>
    public bool IgnoresFieldId(string? fieldId)
    {
        if (string.IsNullOrEmpty(fieldId) || IgnoredFieldIdPatterns.Count == 0) return false;
        foreach (var rule in IgnoredFieldIdPatterns)
        {
            if (string.IsNullOrEmpty(rule.Value)) continue;
            var hit = rule.Kind switch
            {
                FieldIdMatchKind.Contains => fieldId.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
                FieldIdMatchKind.StartsWith => fieldId.StartsWith(rule.Value, StringComparison.OrdinalIgnoreCase),
                FieldIdMatchKind.EndsWith => fieldId.EndsWith(rule.Value, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
            if (hit) return true;
        }
        return false;
    }

    /// <summary>True when differences in the named field property should be ignored.</summary>
    public bool IgnoresProperty(string propertyName)
    {
        if (IgnoredFieldProperties.Count == 0) return false;
        foreach (var p in IgnoredFieldProperties)
            if (string.Equals(p, propertyName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The safe baseline: no destructive operations, no cosmetic overwrites.</summary>
    public static readonly MergePolicy Default = new();
}
