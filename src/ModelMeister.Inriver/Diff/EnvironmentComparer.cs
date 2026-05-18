using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Inriver.Diff;

/// <summary>
/// Compare two <see cref="LiveModel"/> snapshots and produce a structured diff report. Intended
/// for the UI's "Compare environments" view — it is a <i>read-only</i> report, not an applicable
/// change set. To push the differences, scaffold one side to a model project and apply against
/// the other using the regular diff/apply pipeline.
/// </summary>
public static class EnvironmentComparer
{
    private static readonly StringComparer Id = StringComparer.OrdinalIgnoreCase;

    /// <summary>Build the report by diffing <paramref name="left"/> and <paramref name="right"/>.</summary>
    public static EnvironmentDiff Compare(LiveModel left, LiveModel right)
    {
        var diff = new EnvironmentDiff
        {
            LeftUrl = left.EnvironmentUrl,
            RightUrl = right.EnvironmentUrl,
            CapturedLeftUtc = left.CapturedUtc,
            CapturedRightUtc = right.CapturedUtc,
        };

        diff.Languages = SetDiff(left.Languages, right.Languages, x => x);
        diff.EntityTypes = SetDiff(left.EntityTypes, right.EntityTypes, x => x.Id);
        diff.Cvls = SetDiff(left.Cvls, right.Cvls, x => x.Id);
        diff.Categories = SetDiff(left.Categories, right.Categories, x => x.Id);
        diff.Fieldsets = SetDiff(left.Fieldsets, right.Fieldsets, x => x.Id);
        diff.LinkTypes = SetDiff(left.LinkTypes, right.LinkTypes, x => x.Id);
        diff.Roles = SetDiff(left.Roles, right.Roles, x => x.Name);

        // Field-level comparison — flat list of all fields keyed by id.
        var lFields = left.EntityTypes.SelectMany(e => e.Fields).ToList();
        var rFields = right.EntityTypes.SelectMany(e => e.Fields).ToList();
        diff.FieldTypes = SetDiff(lFields, rFields, x => x.Id);

        diff.ChangedFields = FieldsWithChanges(lFields, rFields).ToList();
        diff.ChangedEntityTypes = EntityTypesWithChanges(left, right).ToList();
        diff.ChangedCvls = CvlsWithChanges(left, right).ToList();
        diff.ChangedLinkTypes = LinkTypesWithChanges(left, right).ToList();
        diff.CvlValueChanges = CvlValueChanges(left, right).ToList();
        return diff;
    }

    private static ConceptDelta<string> SetDiff<T>(IEnumerable<T> left, IEnumerable<T> right, Func<T, string> key)
    {
        var leftSet = left.Select(key).ToHashSet(Id);
        var rightSet = right.Select(key).ToHashSet(Id);
        return new ConceptDelta<string>
        {
            OnlyInLeft = leftSet.Except(rightSet, Id).OrderBy(s => s, Id).ToList(),
            OnlyInRight = rightSet.Except(leftSet, Id).OrderBy(s => s, Id).ToList(),
            InBoth = leftSet.Intersect(rightSet, Id).OrderBy(s => s, Id).ToList(),
        };
    }

    private static IEnumerable<FieldChange> FieldsWithChanges(IReadOnlyList<LiveFieldType> left, IReadOnlyList<LiveFieldType> right)
    {
        var rightById = right.ToDictionary(f => f.Id, Id);
        foreach (var lf in left)
        {
            if (!rightById.TryGetValue(lf.Id, out var rf)) continue;
            var diffs = new List<PropertyDiff>();
            if (lf.DataType != rf.DataType) diffs.Add(new("DataType", lf.DataType.ToString(), rf.DataType.ToString()));
            if (lf.Mandatory != rf.Mandatory) diffs.Add(new("Mandatory", Fmt(lf.Mandatory), Fmt(rf.Mandatory)));
            if (lf.Unique != rf.Unique) diffs.Add(new("Unique", Fmt(lf.Unique), Fmt(rf.Unique)));
            if (lf.MultiValue != rf.MultiValue) diffs.Add(new("MultiValue", Fmt(lf.MultiValue), Fmt(rf.MultiValue)));
            if (lf.Hidden != rf.Hidden) diffs.Add(new("Hidden", Fmt(lf.Hidden), Fmt(rf.Hidden)));
            if (lf.ReadOnly != rf.ReadOnly) diffs.Add(new("ReadOnly", Fmt(lf.ReadOnly), Fmt(rf.ReadOnly)));
            if (lf.IsDisplayName != rf.IsDisplayName) diffs.Add(new("IsDisplayName", Fmt(lf.IsDisplayName), Fmt(rf.IsDisplayName)));
            if (lf.IsDisplayDescription != rf.IsDisplayDescription) diffs.Add(new("IsDisplayDescription", Fmt(lf.IsDisplayDescription), Fmt(rf.IsDisplayDescription)));
            if (lf.TrackChanges != rf.TrackChanges) diffs.Add(new("TrackChanges", Fmt(lf.TrackChanges), Fmt(rf.TrackChanges)));
            if (!string.Equals(lf.CategoryId, rf.CategoryId, StringComparison.Ordinal)) diffs.Add(new("CategoryId", lf.CategoryId ?? "", rf.CategoryId ?? ""));
            if (!string.Equals(lf.CvlId, rf.CvlId, StringComparison.Ordinal)) diffs.Add(new("CvlId", lf.CvlId ?? "", rf.CvlId ?? ""));
            if (!string.Equals(lf.DefaultValue ?? string.Empty, rf.DefaultValue ?? string.Empty, StringComparison.Ordinal))
                diffs.Add(new("DefaultValue", lf.DefaultValue ?? "", rf.DefaultValue ?? ""));
            if (!LocaleStringEquals(lf.Name, rf.Name)) diffs.Add(new("Name", LsFlatten(lf.Name), LsFlatten(rf.Name)));
            if (!LocaleStringEquals(lf.Description, rf.Description)) diffs.Add(new("Description", LsFlatten(lf.Description), LsFlatten(rf.Description)));
            if (diffs.Count > 0) yield return new FieldChange(lf.Id, lf.EntityTypeId, diffs);
        }
    }

    private static IEnumerable<EntityTypeChange> EntityTypesWithChanges(LiveModel l, LiveModel r)
    {
        var rById = r.EntityTypes.ToDictionary(e => e.Id, Id);
        foreach (var le in l.EntityTypes)
        {
            if (!rById.TryGetValue(le.Id, out var re)) continue;
            var diffs = new List<PropertyDiff>();
            if (le.IsLinkEntityType != re.IsLinkEntityType) diffs.Add(new("IsLinkEntityType", Fmt(le.IsLinkEntityType), Fmt(re.IsLinkEntityType)));
            if (!string.Equals(le.DisplayNameFieldId ?? "", re.DisplayNameFieldId ?? "", StringComparison.Ordinal))
                diffs.Add(new("DisplayNameField", le.DisplayNameFieldId ?? "", re.DisplayNameFieldId ?? ""));
            if (!string.Equals(le.DisplayDescriptionFieldId ?? "", re.DisplayDescriptionFieldId ?? "", StringComparison.Ordinal))
                diffs.Add(new("DisplayDescriptionField", le.DisplayDescriptionFieldId ?? "", re.DisplayDescriptionFieldId ?? ""));
            if (!LocaleStringEquals(le.Name, re.Name)) diffs.Add(new("Name", LsFlatten(le.Name), LsFlatten(re.Name)));
            if (diffs.Count > 0) yield return new EntityTypeChange(le.Id, diffs);
        }
    }

    private static IEnumerable<CvlChange> CvlsWithChanges(LiveModel l, LiveModel r)
    {
        var rById = r.Cvls.ToDictionary(c => c.Id, Id);
        foreach (var lc in l.Cvls)
        {
            if (!rById.TryGetValue(lc.Id, out var rc)) continue;
            var diffs = new List<PropertyDiff>();
            if (lc.DataType != rc.DataType) diffs.Add(new("DataType", lc.DataType.ToString(), rc.DataType.ToString()));
            if (!string.Equals(lc.ParentId ?? "", rc.ParentId ?? "", StringComparison.Ordinal))
                diffs.Add(new("ParentId", lc.ParentId ?? "", rc.ParentId ?? ""));
            if (lc.CustomValueList != rc.CustomValueList) diffs.Add(new("CustomValueList", Fmt(lc.CustomValueList), Fmt(rc.CustomValueList)));
            if (diffs.Count > 0) yield return new CvlChange(lc.Id, diffs);
        }
    }

    private static IEnumerable<LinkTypeChange> LinkTypesWithChanges(LiveModel l, LiveModel r)
    {
        var rById = r.LinkTypes.ToDictionary(c => c.Id, Id);
        foreach (var ll in l.LinkTypes)
        {
            if (!rById.TryGetValue(ll.Id, out var rl)) continue;
            var diffs = new List<PropertyDiff>();
            if (!string.Equals(ll.SourceEntityTypeId, rl.SourceEntityTypeId, StringComparison.Ordinal))
                diffs.Add(new("Source", ll.SourceEntityTypeId ?? "", rl.SourceEntityTypeId ?? ""));
            if (!string.Equals(ll.TargetEntityTypeId, rl.TargetEntityTypeId, StringComparison.Ordinal))
                diffs.Add(new("Target", ll.TargetEntityTypeId ?? "", rl.TargetEntityTypeId ?? ""));
            if (!string.Equals(ll.LinkEntityTypeId ?? "", rl.LinkEntityTypeId ?? "", StringComparison.Ordinal))
                diffs.Add(new("LinkEntityType", ll.LinkEntityTypeId ?? "", rl.LinkEntityTypeId ?? ""));
            if (diffs.Count > 0) yield return new LinkTypeChange(ll.Id, diffs);
        }
    }

    private static string Fmt(bool b) => b ? "true" : "false";

    private static string LsFlatten(LocaleString? ls)
    {
        if (ls is null) return "";
        if (ls.Values.Count == 0) return ls.DefaultValue ?? "";
        return string.Join("; ", ls.Values.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private static IEnumerable<CvlValueDelta> CvlValueChanges(LiveModel l, LiveModel r)
    {
        foreach (var cvlId in l.Cvls.Select(c => c.Id).Union(r.Cvls.Select(c => c.Id), Id).OrderBy(s => s, Id))
        {
            var lValues = l.Cvls.FirstOrDefault(c => c.Id.Equals(cvlId, StringComparison.OrdinalIgnoreCase))?.Values
                ?? [];
            var rValues = r.Cvls.FirstOrDefault(c => c.Id.Equals(cvlId, StringComparison.OrdinalIgnoreCase))?.Values
                ?? [];
            var lKeys = lValues.Select(v => v.Key).ToHashSet(Id);
            var rKeys = rValues.Select(v => v.Key).ToHashSet(Id);

            var onlyLeft = lKeys.Except(rKeys, Id).OrderBy(s => s, Id).ToList();
            var onlyRight = rKeys.Except(lKeys, Id).OrderBy(s => s, Id).ToList();
            var changedKeys = new List<CvlValueKeyChange>();

            foreach (var key in lKeys.Intersect(rKeys, Id).OrderBy(s => s, Id))
            {
                var lv = lValues.First(v => v.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                var rv = rValues.First(v => v.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                var diffs = new List<PropertyDiff>();
                if (!LocaleStringEquals(lv.Value, rv.Value))
                    diffs.Add(new("Value", LsFlatten(lv.Value), LsFlatten(rv.Value)));
                if (lv.Deactivated != rv.Deactivated)
                    diffs.Add(new("Deactivated", Fmt(lv.Deactivated), Fmt(rv.Deactivated)));
                if (!string.Equals(lv.ParentKey ?? "", rv.ParentKey ?? "", StringComparison.Ordinal))
                    diffs.Add(new("ParentKey", lv.ParentKey ?? "", rv.ParentKey ?? ""));
                if (diffs.Count > 0) changedKeys.Add(new CvlValueKeyChange(key, diffs));
            }

            if (onlyLeft.Count + onlyRight.Count + changedKeys.Count > 0)
                yield return new CvlValueDelta(cvlId, onlyLeft, onlyRight, changedKeys);
        }
    }

    private static bool LocaleStringEquals(LocaleString? a, LocaleString? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (!string.Equals(a.DefaultValue ?? "", b.DefaultValue ?? "", StringComparison.Ordinal)) return false;
        if (a.Values.Count != b.Values.Count) return false;
        foreach (var (lang, val) in a.Values)
            if (!b.Values.TryGetValue(lang, out var other) || !string.Equals(val ?? "", other ?? "", StringComparison.Ordinal))
                return false;
        return true;
    }
}

/// <summary>Structured diff between two <see cref="LiveModel"/> snapshots.</summary>
public sealed class EnvironmentDiff
{
    public string LeftUrl { get; set; } = "";
    public string RightUrl { get; set; } = "";
    public DateTime CapturedLeftUtc { get; set; }
    public DateTime CapturedRightUtc { get; set; }
    public ConceptDelta<string> Languages { get; set; } = new();
    public ConceptDelta<string> EntityTypes { get; set; } = new();
    public ConceptDelta<string> Cvls { get; set; } = new();
    public ConceptDelta<string> Categories { get; set; } = new();
    public ConceptDelta<string> Fieldsets { get; set; } = new();
    public ConceptDelta<string> LinkTypes { get; set; } = new();
    public ConceptDelta<string> Roles { get; set; } = new();
    public ConceptDelta<string> FieldTypes { get; set; } = new();
    public List<FieldChange> ChangedFields { get; set; } = [];
    public List<EntityTypeChange> ChangedEntityTypes { get; set; } = [];
    public List<CvlChange> ChangedCvls { get; set; } = [];
    public List<LinkTypeChange> ChangedLinkTypes { get; set; } = [];
    public List<CvlValueDelta> CvlValueChanges { get; set; } = [];

    public int TotalDifferences =>
        Languages.Total + EntityTypes.Total + Cvls.Total + Categories.Total + Fieldsets.Total +
        LinkTypes.Total + Roles.Total + FieldTypes.Total + ChangedFields.Count + ChangedEntityTypes.Count +
        ChangedCvls.Count + ChangedLinkTypes.Count + CvlValueChanges.Sum(d => d.Total);
}

/// <summary>One concept's three-way partition: only-left, only-right, and the intersection.</summary>
public sealed class ConceptDelta<T>
{
    public List<T> OnlyInLeft { get; set; } = [];
    public List<T> OnlyInRight { get; set; } = [];
    public List<T> InBoth { get; set; } = [];

    /// <summary>Number of items that differ (left-only + right-only; the intersection is not counted).</summary>
    public int Total => OnlyInLeft.Count + OnlyInRight.Count;
}

public sealed record FieldChange(string FieldId, string EntityTypeId, IReadOnlyList<PropertyDiff> Differences);
public sealed record EntityTypeChange(string EntityTypeId, IReadOnlyList<PropertyDiff> Differences);
public sealed record CvlChange(string CvlId, IReadOnlyList<PropertyDiff> Differences);
public sealed record LinkTypeChange(string LinkTypeId, IReadOnlyList<PropertyDiff> Differences);

/// <summary>One property's value on each side of an environment compare. <see cref="Left"/> /
/// <see cref="Right"/> already render as user-visible strings (e.g. "true", "false", a flattened
/// LocaleString).</summary>
public sealed record PropertyDiff(string Property, string Left, string Right);
public sealed record CvlValueDelta(string CvlId, IReadOnlyList<string> OnlyInLeft, IReadOnlyList<string> OnlyInRight, IReadOnlyList<CvlValueKeyChange> Changed)
{
    public int Total => OnlyInLeft.Count + OnlyInRight.Count + Changed.Count;
}

/// <summary>One CVL value key that exists on both sides but whose <c>Value</c>, <c>ParentKey</c>,
/// or <c>Deactivated</c> differ. <see cref="Differences"/> carries the per-property diffs.</summary>
public sealed record CvlValueKeyChange(string Key, IReadOnlyList<PropertyDiff> Differences);
