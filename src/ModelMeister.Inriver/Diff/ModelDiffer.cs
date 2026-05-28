using ModelMeister.Inriver.Mapping;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;

namespace ModelMeister.Inriver.Diff;

/// <summary>
/// Pure-function diff between a code-defined <see cref="LoadedModel"/> and a live
/// <see cref="LiveModel"/>. Produces a <see cref="ModelChangeSet"/> with typed change records.
/// Respects <see cref="MergePolicy"/> for destructive operations.
/// </summary>
/// <remarks>
/// This is a pure function: no I/O, no side effects, deterministic for the same inputs.
/// The applier consumes the result; orchestration lives there, not here.
/// </remarks>
public static class ModelDiffer
{
    private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

    // Categories that are platform-built-ins — never proposed for deletion.
    private static readonly HashSet<string> ProtectedCategories =
        new(["General", "FileInformation"], IdComparer);

    // Roles that are platform-built-ins — never proposed for deletion.
    private static readonly HashSet<string> ProtectedRoles =
        new(["Administrator"], IdComparer);

    /// <summary>Compute the change set required to bring <paramref name="live"/> in line with <paramref name="code"/>.</summary>
    public static ModelChangeSet Diff(LoadedModel code, LiveModel live, MergePolicy? policy = null)
    {
        policy ??= MergePolicy.Default;
        var changes = new List<ModelChange>();
        var warnings = new List<DiffWarning>();

        // Resolve CVL/Category CLR-type -> id lookups once. FieldDiffers needs them to compare
        // bound CVLs and categories: using Type.Name would mismatch when a sanitized class name
        // diverges from the inriver id (e.g. inriver "My-Specs" -> class "MySpecs").
        var cvlIdByClrType = code.Cvls.ToDictionary(c => c.ClrType, c => c.CvlId);
        var categoryIdByClrType = code.Categories.ToDictionary(c => c.ClrType, c => c.CategoryId);

        DiffLanguages(code, live, changes);
        DiffCategories(code, live, changes, policy, warnings);
        DiffCvls(code, live, changes, policy, warnings);
        DiffEntityTypesAndFields(code, live, changes, policy, warnings, cvlIdByClrType, categoryIdByClrType);
        DiffFieldsets(code, live, changes, policy, warnings);
        DiffLinkTypes(code, live, changes, policy, warnings);
        DiffRoles(code, live, changes, policy, warnings);
        DiffCompleteness(code, live, changes, policy, warnings);

        return new ModelChangeSet { Changes = changes, Warnings = warnings };
    }

    /// <summary>
    /// Emit deletions, or — when <see cref="MergePolicy.AllowDeletes"/> is off — record one
    /// <c>DeleteBlocked</c> warning per candidate. This makes "N change(s) suppressed by policy"
    /// surfaceable instead of silently dropping destructive ops, while preserving idempotency:
    /// an in-sync model has no candidates, so emits neither changes nor warnings.
    /// </summary>
    private static void EmitDeletes<T>(
        MergePolicy policy,
        IEnumerable<T> candidates,
        Func<T, ModelChange> toDelete,
        Func<T, string> describeBlocked,
        List<ModelChange> changes,
        List<DiffWarning> warnings)
    {
        foreach (var candidate in candidates)
        {
            if (policy.AllowDeletes) changes.Add(toDelete(candidate));
            else warnings.Add(new DiffWarning("DeleteBlocked", describeBlocked(candidate)));
        }
    }

    // ---------- Languages ----------
    private static void DiffLanguages(LoadedModel code, LiveModel live, List<ModelChange> changes)
    {
        var liveLangs = live.Languages.ToHashSet(IdComparer);
        changes.AddRange(code.Languages
            .Where(l => !liveLangs.Contains(l.IsoCode))
            .Select(l => new AddLanguage(l.IsoCode)));
        // Plan: no language deletes — operators handle languages out-of-band.
    }

    // ---------- Categories ----------
    private static void DiffCategories(LoadedModel code, LiveModel live, List<ModelChange> changes, MergePolicy policy, List<DiffWarning> warnings)
    {
        var liveById = live.Categories.ToDictionary(c => c.Id, IdComparer);
        foreach (var c in code.Categories)
        {
            if (!liveById.TryGetValue(c.CategoryId, out var l))
                changes.Add(new AddCategory(c));
            else if (CategoryDiffers(c, l, policy))
                changes.Add(new UpdateCategory(c));
        }
        var codeIds = code.Categories.Select(c => c.CategoryId).ToHashSet(IdComparer);
        EmitDeletes(policy,
            live.Categories.Where(l => !codeIds.Contains(l.Id) && !ProtectedCategories.Contains(l.Id)),
            l => new DeleteCategory(l.Id),
            l => $"Category '{l.Id}' exists in the environment but not in code — deletion suppressed (AllowDeletes is off).",
            changes, warnings);
    }

    private static bool CategoryDiffers(LoadedCategory c, LiveCategory l, MergePolicy policy) =>
        (policy.OverwriteNamesAndDescriptions && !LsEquals(c.Name, l.Name))
        || (!policy.IgnoreCategoryIndexSortingOnUpdate && c.Index != l.Index);

    // ---------- CVLs ----------
    private static void DiffCvls(LoadedModel code, LiveModel live, List<ModelChange> changes, MergePolicy policy, List<DiffWarning> warnings)
    {
        var liveById = live.Cvls.ToDictionary(c => c.Id, IdComparer);
        foreach (var c in code.Cvls)
        {
            if (!liveById.TryGetValue(c.CvlId, out var l))
            {
                changes.Add(new AddCvl(c));
                changes.AddRange(c.Values.Select(v => new AddCvlValue(c.CvlId, v)));
            }
            else
            {
                if (CvlDiffers(c, l)) changes.Add(new UpdateCvl(c));
                DiffCvlValues(c, l, changes, policy, warnings);
            }
        }
        var codeIds = code.Cvls.Select(c => c.CvlId).ToHashSet(IdComparer);
        EmitDeletes(policy,
            live.Cvls.Where(l => !codeIds.Contains(l.Id)),
            l => new DeleteCvl(l.Id),
            l => $"CVL '{l.Id}' exists in the environment but not in code — deletion suppressed (AllowDeletes is off).",
            changes, warnings);
    }

    private static bool CvlDiffers(LoadedCvl c, LiveCvl l) =>
        c.DataType != l.DataType
        || NullableEquals(c.ParentCvlId, l.ParentId) is false
        || c.CustomValueList != l.CustomValueList;

    private static void DiffCvlValues(LoadedCvl c, LiveCvl l, List<ModelChange> changes, MergePolicy policy, List<DiffWarning> warnings)
    {
        var liveByKey = l.Values.ToDictionary(v => v.Key, IdComparer);
        foreach (var v in c.Values)
        {
            if (!liveByKey.TryGetValue(v.Key, out var lv))
            {
                changes.Add(new AddCvlValue(c.CvlId, v));
                continue;
            }
            if (policy.OverwriteCvlValues
                && (!LsEquals(v.Value, lv.Value) || NullableEquals(v.Parent, lv.ParentKey) is false))
            {
                changes.Add(new UpdateCvlValue(c.CvlId, lv.Id, v));
            }
        }
        var codeKeys = c.Values.Select(v => v.Key).ToHashSet(IdComparer);
        EmitDeletes(policy,
            l.Values.Where(lv => !codeKeys.Contains(lv.Key) && !lv.Deactivated),
            lv => new DeactivateCvlValue(c.CvlId, lv.Id, lv.Key),
            lv => $"CVL value '{c.CvlId}/{lv.Key}' is not in code — deactivation suppressed (AllowDeletes is off).",
            changes, warnings);
    }

    // ---------- Entity types + fields ----------
    private static void DiffEntityTypesAndFields(
        LoadedModel code,
        LiveModel live,
        List<ModelChange> changes,
        MergePolicy policy,
        List<DiffWarning> warnings,
        Dictionary<Type, string> cvlIdByClrType,
        Dictionary<Type, string> categoryIdByClrType)
    {
        var liveById = live.EntityTypes.ToDictionary(e => e.Id, IdComparer);
        foreach (var e in code.EntityTypes)
        {
            if (!liveById.TryGetValue(e.EntityTypeId, out var l))
            {
                changes.Add(new AddEntityType(e));
                changes.AddRange(e.Fields.Select(f => new AddFieldType(f, e)));
            }
            else
            {
                if (EntityTypeDiffers(e, l, policy)) changes.Add(new UpdateEntityType(e));
                DiffFields(e, l, changes, policy, warnings, cvlIdByClrType, categoryIdByClrType);
            }
        }
        var codeIds = code.EntityTypes.Select(e => e.EntityTypeId).ToHashSet(IdComparer);
        EmitDeletes(policy,
            live.EntityTypes.Where(l => !codeIds.Contains(l.Id)),
            l => new DeleteEntityType(l.Id),
            l => $"Entity type '{l.Id}' exists in the environment but not in code — deletion suppressed (AllowDeletes is off).",
            changes, warnings);
    }

    private static bool EntityTypeDiffers(LoadedEntityType e, LiveEntityType l, MergePolicy policy) =>
        (policy.OverwriteNamesAndDescriptions && !LsEquals(e.Name, l.Name))
        || e.IsLinkEntityType != l.IsLinkEntityType;

    private static void DiffFields(
        LoadedEntityType e,
        LiveEntityType l,
        List<ModelChange> changes,
        MergePolicy policy,
        List<DiffWarning> warnings,
        Dictionary<Type, string> cvlIdByClrType,
        Dictionary<Type, string> categoryIdByClrType)
    {
        var liveById = l.Fields.ToDictionary(f => f.Id, IdComparer);
        foreach (var f in e.Fields)
        {
            // Field-id ignore rules suppress every kind of difference (add/datatype/update) for
            // matching ids — see MergePolicy.IgnoredFieldIdPatterns.
            if (policy.IgnoresFieldId(f.Id)) continue;
            if (!liveById.TryGetValue(f.Id, out var lf))
            {
                changes.Add(new AddFieldType(f, e));
                continue;
            }
            if (f.DataType != lf.DataType)
            {
                if (policy.AllowDatatypeChange)
                {
                    changes.Add(new ChangeFieldDatatype(f, e, lf.DataType, f.DataType));
                }
                else
                {
                    warnings.Add(new DiffWarning(
                        "DatatypeChangeBlocked",
                        $"Field {f.Id} datatype {lf.DataType} -> {f.DataType} ignored (AllowDatatypeChange = false)."));
                }
                continue;
            }
            if (FieldDiffers(f, lf, policy, cvlIdByClrType, categoryIdByClrType))
                changes.Add(new UpdateFieldType(f, e));
        }
        var codeFieldIds = e.Fields.Select(f => f.Id).ToHashSet(IdComparer);
        EmitDeletes(policy,
            l.Fields.Where(lf => !codeFieldIds.Contains(lf.Id) && !policy.IgnoresFieldId(lf.Id)),
            lf => new DeleteFieldType(lf.Id),
            lf => $"Field '{lf.Id}' on '{e.EntityTypeId}' is not in code — deletion suppressed (AllowDeletes is off).",
            changes, warnings);
    }

    /// <summary>
    /// True when the code-side field differs from the live field along any property that the
    /// applier can update. Honours read-through semantics for nullable code-side properties —
    /// see the long comment inside.
    /// </summary>
    private static bool FieldDiffers(
        LoadedField f,
        LiveFieldType lf,
        MergePolicy policy,
        Dictionary<Type, string> cvlIdByClrType,
        Dictionary<Type, string> categoryIdByClrType)
    {
        var ff = f.Field;

        // Field-type id ignore rules: a matching id suppresses all of this field's differences.
        if (policy.IgnoresFieldId(f.Id)) return false;

        // Straight code-vs-live comparisons for non-nullable / required code-side properties.
        // Each check is gated by policy.IgnoresProperty(...) so a user can suppress noise on
        // specific properties (e.g. TrackChanges, Index) via settings.
        if (policy.OverwriteNamesAndDescriptions)
        {
            if (!policy.IgnoresProperty("Name") && !LsEquals(f.Name, lf.Name)) return true;
            if (!policy.IgnoresProperty("Description") && ff.Description is not null && !LsEquals(ff.Description, lf.Description)) return true;
        }
        if (!policy.IgnoresProperty("Mandatory") && ff.Mandatory != lf.Mandatory) return true;
        if (!policy.IgnoresProperty("Unique") && ff.Unique != lf.Unique) return true;
        if (!policy.IgnoresProperty("ReadOnly") && ff.ReadOnly != lf.ReadOnly) return true;
        if (!policy.IgnoresProperty("Hidden") && ff.Hidden != lf.Hidden) return true;
        if (!policy.IgnoresProperty("MultiValue") && ff.MultiValue != lf.MultiValue) return true;
        if (!policy.IgnoresProperty("IsDisplayName") && ff.IsDisplayName != lf.IsDisplayName) return true;
        if (!policy.IgnoresProperty("IsDisplayDescription") && ff.IsDisplayDescription != lf.IsDisplayDescription) return true;
        if (!policy.IgnoresProperty("SupportsExpression") && ff.SupportsExpression != lf.ExpressionSupport) return true;

        // Read-through invariant: for nullable code-side properties (ExcludeFromDefaultView,
        // Index, DefaultValue, Category, CvlId) an UNSET code value means "leave inriver's value
        // alone" — NOT "set to default". The mapper (FieldTypeMapper.ToInriver) agrees: when the
        // code value is null, it falls back to the live read-through. If the two disagree, diff ->
        // apply -> diff oscillates. The scaffolder does not emit these on every field, so a
        // stricter comparison would cause every scaffolded field to diff against its source env.
        // TrackChanges is NOT read-through: it defaults to true (stamped by ModelLoader), so the
        // code model is authoritative and the comparison below always runs.

        // Field.Cvl / Field.Category MUST be read off the base Field type — the derived
        // Field<TData, TBinding> ctors stamp these properties; the base is authoritative.

        if (!policy.IgnoresProperty("Category") && ff.Category is not null)
        {
            // Resolve the code-side category id via the CLR-type lookup so a sanitized class
            // name doesn't silently overwrite the real inriver id.
            var codeCategoryId = categoryIdByClrType.GetValueOrDefault(ff.Category, ff.Category.Name);
            if (codeCategoryId != (lf.CategoryId ?? string.Empty)) return true;
        }

        if (!policy.IgnoresProperty("Index") && !policy.IgnoreFieldIndexSortingOnUpdate && ff.Index is { } codeIndex && codeIndex != lf.Index) return true;
        if (!policy.IgnoresProperty("TrackChanges") && ff.TrackChanges is { } codeTrack && codeTrack != lf.TrackChanges) return true;
        if (!policy.IgnoresProperty("ExcludeFromDefaultView") && ff.ExcludeFromDefaultView is { } codeExcl && codeExcl != lf.ExcludeFromDefaultView) return true;

        // Default value AND default expression are the same inriver slot (FieldType.DefaultValue —
        // an expression is just a =-prefixed string). Collapse the code side to that single string
        // and compare with whitespace-tolerant expression equality. Read-through: a null code default
        // (neither DefaultValue nor DefaultExpression set) leaves inriver's value alone.
        if (!policy.IgnoresProperty("DefaultValue") && !policy.IgnoresProperty("DefaultExpression")
            && FieldTypeMapper.CodeDefaultValue(ff) is { } codeDefault
            && !FieldTypeMapper.DefaultValuesEqual(codeDefault, FieldTypeMapper.LiveDefaultValue(lf)))
        {
            return true;
        }

        // CVL re-bind: a code-side CVL CLR-type maps to a CvlId via the model-wide lookup.
        // Without this, an in-place CVL swap (the field still exists, but bound to a different CVL)
        // would silently never produce an update.
        if (!policy.IgnoresProperty("Cvl"))
        {
            var codeCvlId = ff.Cvl is null
                ? null
                : cvlIdByClrType.GetValueOrDefault(ff.Cvl, ff.Cvl.Name);
            if (!string.Equals(codeCvlId ?? string.Empty, lf.CvlId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // (DefaultExpression is folded into the DefaultValue comparison above — same inriver slot.)

        return false;
    }

    // ---------- Fieldsets ----------
    private static void DiffFieldsets(LoadedModel code, LiveModel live, List<ModelChange> changes, MergePolicy policy, List<DiffWarning> warnings)
    {
        var liveById = live.Fieldsets.ToDictionary(f => f.Id, IdComparer);
        foreach (var f in code.Fieldsets)
        {
            if (!liveById.TryGetValue(f.FieldsetId, out var l))
                changes.Add(new AddFieldset(f));
            else if (FieldsetDiffers(f, l, policy))
                changes.Add(new UpdateFieldset(f));
        }

        // Field-to-fieldset assignments. Pre-build the CLR-type -> fieldset-id lookup once.
        var fieldsetByClrType = code.Fieldsets.ToDictionary(s => s.ClrType, s => s.FieldsetId);
        var fieldsetsByFieldId = code.EntityTypes
            .SelectMany(e => e.Fields)
            .ToDictionary(
                f => f.Id,
                f => f.Field.Fieldsets
                    .Select(t => fieldsetByClrType.GetValueOrDefault(t))
                    .Where(id => id is not null)
                    .Select(id => id!)
                    .ToHashSet(IdComparer),
                IdComparer);

        var liveFieldsByFieldset = live.Fieldsets.ToDictionary(
            s => s.Id,
            s => s.FieldTypeIds.ToHashSet(IdComparer),
            IdComparer);

        foreach (var setId in code.Fieldsets.Select(s => s.FieldsetId))
        {
            var wanted = fieldsetsByFieldId
                .Where(kvp => kvp.Value.Contains(setId))
                .Select(kvp => kvp.Key)
                .ToHashSet(IdComparer);
            var have = liveFieldsByFieldset.GetValueOrDefault(setId) ?? new HashSet<string>(IdComparer);

            changes.AddRange(wanted.Except(have, IdComparer)
                .Select(add => new AddFieldToFieldset(setId, add)));
            var thisSetId = setId;
            EmitDeletes(policy,
                have.Except(wanted, IdComparer),
                rem => new RemoveFieldFromFieldset(thisSetId, rem),
                rem => $"Field '{rem}' would be removed from fieldset '{thisSetId}' — suppressed (AllowDeletes is off).",
                changes, warnings);
        }

        var codeIds = code.Fieldsets.Select(f => f.FieldsetId).ToHashSet(IdComparer);
        EmitDeletes(policy,
            live.Fieldsets.Where(l => !codeIds.Contains(l.Id)),
            l => new DeleteFieldset(l.Id),
            l => $"Fieldset '{l.Id}' exists in the environment but not in code — deletion suppressed (AllowDeletes is off).",
            changes, warnings);
    }

    private static bool FieldsetDiffers(LoadedFieldset f, LiveFieldset l, MergePolicy policy)
    {
        if (policy.OverwriteNamesAndDescriptions
            && (!LsEquals(f.Name, l.Name) || !LsEquals(f.Description, l.Description)))
            return true;
        return f.EntityTypeId != l.EntityTypeId;
    }

    // ---------- Completeness ----------
    // Compared at the definition (per entity type) grain via canonical projections so inriver's numeric
    // ids never enter the comparison. A structural difference becomes one Update carrying the live def id.
    private static void DiffCompleteness(LoadedModel code, LiveModel live, List<ModelChange> changes, MergePolicy policy, List<DiffWarning> warnings)
    {
        var liveByEntity = live.CompletenessDefinitions
            .GroupBy(d => d.EntityTypeId, IdComparer)
            .ToDictionary(g => g.Key, g => g.First(), IdComparer);

        var codeEntities = new HashSet<string>(IdComparer);
        foreach (var def in code.CompletenessDefinitions)
        {
            codeEntities.Add(def.EntityTypeId);
            if (!liveByEntity.TryGetValue(def.EntityTypeId, out var liveDef))
                changes.Add(new AddCompletenessDefinition(def));
            else if (CompletenessProjection.FromLoaded(def) != CompletenessProjection.FromLive(liveDef))
                changes.Add(new UpdateCompletenessDefinition(def, liveDef.Id));
        }

        EmitDeletes(policy,
            live.CompletenessDefinitions.Where(d => !codeEntities.Contains(d.EntityTypeId)),
            d => new DeleteCompletenessDefinition(d.EntityTypeId, d.Id),
            d => $"Completeness definition for '{d.EntityTypeId}' is not in code — deletion suppressed (AllowDeletes is off).",
            changes, warnings);
    }

    // ---------- Link types ----------
    private static void DiffLinkTypes(LoadedModel code, LiveModel live, List<ModelChange> changes, MergePolicy policy, List<DiffWarning> warnings)
    {
        var liveById = live.LinkTypes.ToDictionary(l => l.Id, IdComparer);
        foreach (var l in code.LinkTypes)
        {
            if (!liveById.TryGetValue(l.LinkTypeId, out var lv))
                changes.Add(new AddLinkType(l));
            else if (LinkTypeDiffers(l, lv, policy))
                changes.Add(new UpdateLinkType(l));
        }
        var codeIds = code.LinkTypes.Select(l => l.LinkTypeId).ToHashSet(IdComparer);
        EmitDeletes(policy,
            live.LinkTypes.Where(l => !codeIds.Contains(l.Id)),
            l => new DeleteLinkType(l.Id),
            l => $"Link type '{l.Id}' exists in the environment but not in code — deletion suppressed (AllowDeletes is off).",
            changes, warnings);
    }

    private static bool LinkTypeDiffers(LoadedLinkType l, LiveLinkType lv, MergePolicy policy)
    {
        if (l.SourceEntityTypeId != lv.SourceEntityTypeId) return true;
        if (l.TargetEntityTypeId != lv.TargetEntityTypeId) return true;
        if (NullableEquals(l.LinkEntityTypeId, lv.LinkEntityTypeId) is false) return true;
        if (policy.OverwriteNamesAndDescriptions
            && (!LsEquals(LinkTypeMapper.EffectiveSourceName(l), lv.SourceName)
                || !LsEquals(LinkTypeMapper.EffectiveTargetName(l), lv.TargetName)))
            return true;
        return !policy.IgnoreLinkTypeIndexSortingOnUpdate && l.Index != lv.Index;
    }

    // ---------- Roles ----------
    private static void DiffRoles(LoadedModel code, LiveModel live, List<ModelChange> changes, MergePolicy policy, List<DiffWarning> warnings)
    {
        var liveByName = live.Roles.ToDictionary(r => r.Name, IdComparer);
        foreach (var r in code.Roles)
        {
            if (!liveByName.TryGetValue(r.Name, out var lv))
            {
                changes.Add(new AddRole(r));
                // Permission concept must already exist on inriver — applier resolves it lazily.
                changes.AddRange(r.PermissionNames
                    .Select(pName => new AddPermissionToRole(0, 0, pName, r.Name)));
                continue;
            }

            if (policy.OverwriteNamesAndDescriptions
                && (r.Description ?? string.Empty) != (lv.Description ?? string.Empty))
            {
                changes.Add(new UpdateRole(r));
            }

            var liveSet = lv.Permissions.Select(p => p.Name).ToHashSet(IdComparer);
            var codeSet = r.PermissionNames.ToHashSet(IdComparer);

            changes.AddRange(codeSet.Except(liveSet, IdComparer)
                .Select(add => new AddPermissionToRole(lv.Id, 0, add, r.Name)));

            var liveRole = lv;
            var codeRole = r;
            EmitDeletes(policy,
                liveSet.Except(codeSet, IdComparer),
                rem => new RemovePermissionFromRole(liveRole.Id, liveRole.Permissions.First(p => p.Name.Equals(rem, StringComparison.OrdinalIgnoreCase)).Id, rem, codeRole.Name),
                rem => $"Permission '{rem}' would be removed from role '{codeRole.Name}' — suppressed (AllowDeletes is off).",
                changes, warnings);
        }

        var codeNames = code.Roles.Select(r => r.Name).ToHashSet(IdComparer);
        EmitDeletes(policy,
            live.Roles.Where(l => !codeNames.Contains(l.Name) && !ProtectedRoles.Contains(l.Name)),
            l => new DeleteRole(l.Id, l.Name),
            l => $"Role '{l.Name}' exists in the environment but not in code — deletion suppressed (AllowDeletes is off).",
            changes, warnings);
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Semantic equality for <see cref="LocaleString"/>: two values are equal iff <c>For(iso)</c>
    /// returns the same string for every locale referenced on either side. This matches what
    /// <see cref="LocaleStringMapper.ToInriver"/> writes (which falls back to <c>DefaultValue</c>
    /// when a locale isn't explicitly set), so a compact <c>new LocaleString("X")</c> in code is
    /// equivalent to a live LocaleString whose Values dictionary happens to repeat <c>"X"</c>
    /// for each language. Prevents cosmetic-only diffs after scaffolding.
    /// </summary>
    private static bool LsEquals(LocaleString a, LocaleString b)
    {
        if (!string.Equals(a.DefaultValue, b.DefaultValue, StringComparison.Ordinal)) return false;
        var allKeys = a.Values.Keys.Concat(b.Values.Keys).ToHashSet(IdComparer);
        return allKeys.All(k => string.Equals(a.For(k), b.For(k), StringComparison.Ordinal));
    }

    /// <summary>Treats null and empty-string as equivalent for nullable id-style strings.</summary>
    private static bool NullableEquals(string? a, string? b) =>
        string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
}
