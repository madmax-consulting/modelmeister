using System;
using System.Collections.Generic;
using System.Linq;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Mapping;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Ui.Services;

/// <summary>One property-level "live → code" delta surfaced in the Compare details pane.</summary>
public sealed record PropertyDelta(string Property, string LiveValue, string CodeValue);

/// <summary>
/// Resolves a <see cref="ModelChange"/> against the current <see cref="LiveModel"/> snapshot to
/// produce a property-by-property delta list. For Updates this mirrors the comparison logic in
/// <c>ModelDiffer</c>. For Adds we instead emit the full property set of the new concept so the
/// user can review everything that will be created (Live column shows <see cref="None"/>).
/// </summary>
public static class ChangeDetails
{
    /// <summary>Sentinel shown in the Live column when the concept does not yet exist (Add cases).</summary>
    private const string None = "—";

    /// <summary>Returns property deltas for Updates, or the full property set for Adds. Deletes return empty.</summary>
    /// <param name="policy">Active merge policy — its field-id and field-property ignore rules
    /// suppress the matching deltas so the details pane mirrors what the diff actually applies.</param>
    public static IReadOnlyList<PropertyDelta> For(ModelChange change, LiveModel live, MergePolicy? policy = null) => change switch
    {
        UpdateFieldType u    => FieldDeltas(u, live, policy ?? MergePolicy.Default),
        UpdateEntityType u   => EntityDeltas(u, live),
        UpdateLinkType u     => LinkDeltas(u, live, policy ?? MergePolicy.Default),
        UpdateCategory u     => CategoryDeltas(u, live, policy ?? MergePolicy.Default),
        UpdateFieldset u     => FieldsetDeltas(u, live),
        UpdateCvl u          => CvlDeltas(u, live),
        ChangeFieldDatatype d => [new PropertyDelta("DataType", d.FromType.ToString(), d.ToType.ToString())],

        AddFieldType a       => AddFieldProperties(a),
        AddEntityType a      => AddEntityProperties(a),
        AddLinkType a        => AddLinkProperties(a),
        AddCategory a        => AddCategoryProperties(a),
        AddFieldset a        => AddFieldsetProperties(a),
        AddCvl a             => AddCvlProperties(a),
        AddCvlValue a        => AddCvlValueProperties(a),
        AddRole a            => AddRoleProperties(a),

        _ => [],
    };

    private static List<PropertyDelta> FieldDeltas(UpdateFieldType u, LiveModel live, MergePolicy policy)
    {
        // Whole-field suppression mirrors the differ — an ignored id shows no deltas.
        if (policy.IgnoresFieldId(u.Field.Id)) return new List<PropertyDelta>();

        var deltas = new List<PropertyDelta>();
        var owner = live.EntityTypes.FirstOrDefault(e => string.Equals(e.Id, u.Owner.EntityTypeId, StringComparison.OrdinalIgnoreCase));
        var lf = owner?.Fields.FirstOrDefault(f => string.Equals(f.Id, u.Field.Id, StringComparison.OrdinalIgnoreCase));
        if (lf is null)
        {
            deltas.Add(new PropertyDelta("(field not found in live snapshot)", $"{u.Owner.EntityTypeId}/{u.Field.Id}", ""));
            return deltas;
        }

        var f = u.Field.Field;
        Add(deltas, "Name", lf.Name, f.Name ?? new LocaleString(u.Field.PropertyName));
        Add(deltas, "Description", lf.Description, f.Description);
        Add(deltas, "Mandatory", lf.Mandatory, f.Mandatory);
        Add(deltas, "Unique", lf.Unique, f.Unique);
        Add(deltas, "ReadOnly", lf.ReadOnly, f.ReadOnly);
        Add(deltas, "Hidden", lf.Hidden, f.Hidden);
        Add(deltas, "MultiValue", lf.MultiValue, f.MultiValue);
        Add(deltas, "IsDisplayName", lf.IsDisplayName, f.IsDisplayName);
        Add(deltas, "IsDisplayDescription", lf.IsDisplayDescription, f.IsDisplayDescription);
        Add(deltas, "SupportsExpression", lf.ExpressionSupport, f.SupportsExpression);
        Add(deltas, "Category", lf.CategoryId ?? "", ResolveCategoryId(f.Category));
        if (!policy.IgnoreFieldIndexSortingOnUpdate) Add(deltas, "Index", lf.Index, f.Index ?? 0);
        if (f.TrackChanges is { } trk) Add(deltas, "TrackChanges", lf.TrackChanges, trk);
        if (f.ExcludeFromDefaultView is { } excl) Add(deltas, "ExcludeFromDefaultView", lf.ExcludeFromDefaultView, excl);
        Add(deltas, "Cvl", lf.CvlId ?? "", f.Cvl?.Name ?? "");

        // Default value and default expression share one inriver slot (FieldType.DefaultValue, an
        // expression being a =-prefixed string). Show a single row, only when it actually differs
        // under whitespace-tolerant expression equality — mirrors ModelDiffer.
        var codeDefault = FieldTypeMapper.CodeDefaultValue(f);
        var liveDefault = FieldTypeMapper.LiveDefaultValue(lf);
        if (codeDefault is not null && !FieldTypeMapper.DefaultValuesEqual(codeDefault, liveDefault))
            deltas.Add(new PropertyDelta("DefaultValue", liveDefault ?? "", codeDefault));

        // Drop deltas for properties the policy ignores, so the details pane matches the diff.
        return deltas.Where(d => !policy.IgnoresProperty(d.Property)).ToList();
    }

    private static List<PropertyDelta> EntityDeltas(UpdateEntityType u, LiveModel live)
    {
        var deltas = new List<PropertyDelta>();
        var le = live.EntityTypes.FirstOrDefault(e => e.Id == u.EntityType.EntityTypeId);
        if (le is null) return deltas;

        var e = u.EntityType;
        Add(deltas, "Name", le.Name, e.Name);
        Add(deltas, "IsLinkEntityType", le.IsLinkEntityType, e.IsLinkEntityType);

        var codeDisplayName = e.Fields.FirstOrDefault(f => f.Field.IsDisplayName)?.Id ?? "";
        var codeDisplayDesc = e.Fields.FirstOrDefault(f => f.Field.IsDisplayDescription)?.Id ?? "";
        Add(deltas, "DisplayNameField", le.DisplayNameFieldId ?? "", codeDisplayName);
        Add(deltas, "DisplayDescriptionField", le.DisplayDescriptionFieldId ?? "", codeDisplayDesc);
        return deltas;
    }

    private static List<PropertyDelta> LinkDeltas(UpdateLinkType u, LiveModel live, MergePolicy policy)
    {
        var deltas = new List<PropertyDelta>();
        var ll = live.LinkTypes.FirstOrDefault(x => x.Id == u.LinkType.LinkTypeId);
        if (ll is null) return deltas;

        var l = u.LinkType;
        Add(deltas, "SourceEntityType", ll.SourceEntityTypeId, l.SourceEntityTypeId);
        Add(deltas, "TargetEntityType", ll.TargetEntityTypeId, l.TargetEntityTypeId);
        Add(deltas, "LinkEntityType", ll.LinkEntityTypeId ?? "", l.LinkEntityTypeId ?? "");
        if (!policy.IgnoreLinkTypeIndexSortingOnUpdate) Add(deltas, "Index", ll.Index, l.Index);
        Add(deltas, "SourceName", ll.SourceName, l.SourceName);
        Add(deltas, "TargetName", ll.TargetName, l.TargetName);
        return deltas;
    }

    private static List<PropertyDelta> CategoryDeltas(UpdateCategory u, LiveModel live, MergePolicy policy)
    {
        var deltas = new List<PropertyDelta>();
        var lc = live.Categories.FirstOrDefault(x => x.Id == u.Category.CategoryId);
        if (lc is null) return deltas;
        Add(deltas, "Name", lc.Name, u.Category.Name);
        if (!policy.IgnoreCategoryIndexSortingOnUpdate) Add(deltas, "Index", lc.Index, u.Category.Index);
        return deltas;
    }

    private static List<PropertyDelta> FieldsetDeltas(UpdateFieldset u, LiveModel live)
    {
        var deltas = new List<PropertyDelta>();
        var ls = live.Fieldsets.FirstOrDefault(x => x.Id == u.Fieldset.FieldsetId);
        if (ls is null) return deltas;
        Add(deltas, "Name", ls.Name, u.Fieldset.Name);
        Add(deltas, "Description", ls.Description, u.Fieldset.Description);
        Add(deltas, "EntityType", ls.EntityTypeId, u.Fieldset.EntityTypeId);
        return deltas;
    }

    private static List<PropertyDelta> CvlDeltas(UpdateCvl u, LiveModel live)
    {
        var deltas = new List<PropertyDelta>();
        var lc = live.Cvls.FirstOrDefault(x => x.Id == u.Cvl.CvlId);
        if (lc is null) return deltas;
        Add(deltas, "DataType", lc.DataType, u.Cvl.DataType);
        Add(deltas, "Parent", lc.ParentId ?? "", u.Cvl.ParentCvlId ?? "");
        Add(deltas, "CustomValueList", lc.CustomValueList, u.Cvl.CustomValueList);
        return deltas;
    }

    private static void Add(List<PropertyDelta> deltas, string name, LocaleString live, LocaleString? code)
    {
        var liveStr = Render(live);
        var codeStr = Render(code);
        if (!string.Equals(liveStr, codeStr, StringComparison.Ordinal))
            deltas.Add(new PropertyDelta(name, liveStr, codeStr));
    }

    private static void Add(List<PropertyDelta> deltas, string name, object? live, object? code)
    {
        var liveStr = live?.ToString() ?? "";
        var codeStr = code?.ToString() ?? "";
        if (!string.Equals(liveStr, codeStr, StringComparison.Ordinal))
            deltas.Add(new PropertyDelta(name, liveStr, codeStr));
    }

    // Mirrors FieldTypeMapper.ResolveCategoryId for display: prefer the instance's CategoryId
    // over Type.Name so a sanitized class name doesn't show as a mismatch against the live id.
    private static string ResolveCategoryId(Type? categoryClrType)
    {
        if (categoryClrType is null) return "";
        try
        {
            var instance = (Category)Activator.CreateInstance(categoryClrType)!;
            return instance.CategoryId;
        }
        catch
        {
            return categoryClrType.Name;
        }
    }

    private static string Render(LocaleString? ls)
    {
        if (ls is null) return "";
        if (ls.Values.Count == 0) return ls.DefaultValue ?? "";
        var pairs = string.Join(", ", ls.Values.Select(kv => $"{kv.Key}=\"{kv.Value}\""));
        return $"\"{ls.DefaultValue}\"  ({pairs})";
    }

    // ----- Add: emit every property of the incoming concept (Live column = None sentinel) -----

    private static List<PropertyDelta> AddFieldProperties(AddFieldType a)
    {
        var lf = a.Field;
        var f = lf.Field;
        var deltas = new List<PropertyDelta>
        {
            Set("EntityType", lf.EntityTypeId),
            Set("FieldTypeId", lf.Id),
            Set("DataType", lf.DataType),
            Set("Name", Render(f.Name ?? new LocaleString(lf.PropertyName))),
            Set("Description", Render(f.Description)),
            Set("Mandatory", f.Mandatory),
            Set("Unique", f.Unique),
            Set("ReadOnly", f.ReadOnly),
            Set("Hidden", f.Hidden),
            Set("MultiValue", f.MultiValue),
            Set("IsDisplayName", f.IsDisplayName),
            Set("IsDisplayDescription", f.IsDisplayDescription),
            Set("SupportsExpression", f.SupportsExpression),
            Set("Category", ResolveCategoryId(f.Category)),
            Set("Index", f.Index ?? 0),
            Set("Cvl", f.Cvl?.Name ?? ""),
        };
        // Single inriver slot: literal default or rendered =expression.
        if (FieldTypeMapper.CodeDefaultValue(f) is { } def) deltas.Add(Set("DefaultValue", def));
        if (f.TrackChanges is { } trk) deltas.Add(Set("TrackChanges", trk));
        if (f.ExcludeFromDefaultView is { } excl) deltas.Add(Set("ExcludeFromDefaultView", excl));
        if (lf.SourceLocation is { } src) deltas.Add(Set("Source", src));
        return deltas;
    }

    private static List<PropertyDelta> AddEntityProperties(AddEntityType a)
    {
        var e = a.EntityType;
        var displayName = e.Fields.FirstOrDefault(f => f.Field.IsDisplayName)?.Id ?? "";
        var displayDesc = e.Fields.FirstOrDefault(f => f.Field.IsDisplayDescription)?.Id ?? "";
        return
        [
            Set("EntityTypeId", e.EntityTypeId),
            Set("Name", Render(e.Name)),
            Set("IsLinkEntityType", e.IsLinkEntityType),
            Set("DisplayNameField", displayName),
            Set("DisplayDescriptionField", displayDesc),
            Set("FieldCount", e.Fields.Count),
        ];
    }

    private static List<PropertyDelta> AddLinkProperties(AddLinkType a)
    {
        var l = a.LinkType;
        return
        [
            Set("LinkTypeId", l.LinkTypeId),
            Set("SourceEntityType", l.SourceEntityTypeId),
            Set("TargetEntityType", l.TargetEntityTypeId),
            Set("LinkEntityType", l.LinkEntityTypeId ?? ""),
            Set("Index", l.Index),
            Set("SourceName", Render(l.SourceName)),
            Set("TargetName", Render(l.TargetName)),
        ];
    }

    private static List<PropertyDelta> AddCategoryProperties(AddCategory a)
    {
        var c = a.Category;
        return
        [
            Set("CategoryId", c.CategoryId),
            Set("Name", Render(c.Name)),
            Set("Index", c.Index),
        ];
    }

    private static List<PropertyDelta> AddFieldsetProperties(AddFieldset a)
    {
        var s = a.Fieldset;
        return
        [
            Set("FieldsetId", s.FieldsetId),
            Set("Name", Render(s.Name)),
            Set("Description", Render(s.Description)),
            Set("EntityType", s.EntityTypeId),
            Set("Index", s.Index),
        ];
    }

    private static List<PropertyDelta> AddCvlProperties(AddCvl a)
    {
        var c = a.Cvl;
        return
        [
            Set("CvlId", c.CvlId),
            Set("DataType", c.DataType),
            Set("Parent", c.ParentCvlId ?? ""),
            Set("CustomValueList", c.CustomValueList),
            Set("EntityType", c.EntityTypeId ?? ""),
            Set("ValueCount", c.Values.Count),
        ];
    }

    private static List<PropertyDelta> AddCvlValueProperties(AddCvlValue a)
    {
        var v = a.Value;
        return
        [
            Set("CvlId", a.CvlId),
            Set("Key", v.Key),
            Set("Value", Render(v.Value)),
            Set("Parent", v.Parent ?? ""),
            Set("Index", v.Index),
            Set("Deactivated", v.Deactivated),
        ];
    }

    private static List<PropertyDelta> AddRoleProperties(AddRole a)
    {
        var r = a.Role;
        return
        [
            Set("Name", r.Name),
            Set("Description", r.Description),
            Set("PermissionCount", r.PermissionNames.Count),
            Set("Permissions", string.Join(", ", r.PermissionNames)),
        ];
    }

    private static PropertyDelta Set(string property, object? code) =>
        new(property, None, code?.ToString() ?? "");
}
