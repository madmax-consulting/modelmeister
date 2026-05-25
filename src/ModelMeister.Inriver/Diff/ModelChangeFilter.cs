using System;
using System.Collections.Generic;
using System.Linq;

namespace ModelMeister.Inriver.Diff;

/// <summary>The kind of concept a <see cref="PromoteScope"/> targets.</summary>
public enum PromoteConcept
{
    EntityType,
    Field,
    Cvl,
    CvlValue,
    Category,
    Fieldset,
    LinkType,
}

/// <summary>
/// Identifies a single concept to promote env→env. The promotion unit is the whole concept the
/// row belongs to — e.g. an entity-type scope bundles that type's field-type changes, a CVL scope
/// bundles its value changes.
/// </summary>
public readonly record struct PromoteScope(PromoteConcept Concept, string Id, string? EntityTypeId = null, string? CvlKey = null);

/// <summary>
/// Narrows a full <see cref="ModelChangeSet"/> (produced by <see cref="ModelDiffer"/> against a
/// target env) down to the change(s) belonging to one or more <see cref="PromoteScope"/>s. The
/// resulting set is handed to <c>ChangeApplier</c> as-is — the applier re-sorts by its fixed
/// ApplyOrder, so a filtered subset still applies in dependency-correct order.
/// </summary>
public static class ModelChangeFilter
{
    private static readonly StringComparer Id = StringComparer.OrdinalIgnoreCase;
    private static bool Eq(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>Project <paramref name="full"/> to the changes belonging to <paramref name="scope"/>.</summary>
    public static ModelChangeSet ForConcept(ModelChangeSet full, PromoteScope scope) =>
        ForConcepts(full, new[] { scope });

    /// <summary>Project <paramref name="full"/> to the changes belonging to any of <paramref name="scopes"/>.</summary>
    public static ModelChangeSet ForConcepts(ModelChangeSet full, IEnumerable<PromoteScope> scopes)
    {
        var list = scopes.ToList();
        var kept = full.Changes.Where(c => list.Any(s => Matches(c, s))).ToList();
        return new ModelChangeSet { Changes = kept, Warnings = full.Warnings };
    }

    private static bool Matches(ModelChange change, PromoteScope scope) => scope.Concept switch
    {
        PromoteConcept.EntityType => change switch
        {
            AddEntityType a => Eq(a.EntityType.EntityTypeId, scope.Id),
            UpdateEntityType u => Eq(u.EntityType.EntityTypeId, scope.Id),
            DeleteEntityType d => Eq(d.Id, scope.Id),
            // Promoting an entity type carries its field changes so a brand-new type arrives populated.
            AddFieldType a => Eq(a.Owner.EntityTypeId, scope.Id),
            UpdateFieldType u => Eq(u.Owner.EntityTypeId, scope.Id),
            ChangeFieldDatatype c => Eq(c.Owner.EntityTypeId, scope.Id),
            _ => false,
        },

        PromoteConcept.Field => change switch
        {
            AddFieldType a => Eq(a.Field.Id, scope.Id),
            UpdateFieldType u => Eq(u.Field.Id, scope.Id),
            ChangeFieldDatatype c => Eq(c.Field.Id, scope.Id),
            DeleteFieldType d => Eq(d.Id, scope.Id),
            _ => false,
        },

        PromoteConcept.Cvl => change switch
        {
            AddCvl a => Eq(a.Cvl.CvlId, scope.Id),
            UpdateCvl u => Eq(u.Cvl.CvlId, scope.Id),
            DeleteCvl d => Eq(d.Id, scope.Id),
            // The CVL's values ride along with the definition.
            AddCvlValue a => Eq(a.CvlId, scope.Id),
            UpdateCvlValue u => Eq(u.CvlId, scope.Id),
            DeactivateCvlValue d => Eq(d.CvlId, scope.Id),
            _ => false,
        },

        PromoteConcept.CvlValue => change switch
        {
            AddCvlValue a => Eq(a.CvlId, scope.Id) && Eq(a.Value.Key, scope.CvlKey),
            UpdateCvlValue u => Eq(u.CvlId, scope.Id) && Eq(u.Value.Key, scope.CvlKey),
            DeactivateCvlValue d => Eq(d.CvlId, scope.Id) && Eq(d.Key, scope.CvlKey),
            _ => false,
        },

        PromoteConcept.Category => change switch
        {
            AddCategory a => Eq(a.Category.CategoryId, scope.Id),
            UpdateCategory u => Eq(u.Category.CategoryId, scope.Id),
            DeleteCategory d => Eq(d.Id, scope.Id),
            _ => false,
        },

        PromoteConcept.Fieldset => change switch
        {
            AddFieldset a => Eq(a.Fieldset.FieldsetId, scope.Id),
            UpdateFieldset u => Eq(u.Fieldset.FieldsetId, scope.Id),
            DeleteFieldset d => Eq(d.Id, scope.Id),
            AddFieldToFieldset a => Eq(a.FieldsetId, scope.Id),
            RemoveFieldFromFieldset r => Eq(r.FieldsetId, scope.Id),
            _ => false,
        },

        PromoteConcept.LinkType => change switch
        {
            AddLinkType a => Eq(a.LinkType.LinkTypeId, scope.Id),
            UpdateLinkType u => Eq(u.LinkType.LinkTypeId, scope.Id),
            DeleteLinkType d => Eq(d.Id, scope.Id),
            _ => false,
        },

        _ => false,
    };
}
