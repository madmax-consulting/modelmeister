namespace ModelMeister.Inriver.WorkAreas.Query;

/// <summary>
/// Flags references in a query (entity types, field types, link types) that don't exist in a given
/// environment's <see cref="QueryMetadata"/>. Used (a) live in the builder against the connected env, and
/// (b) in Compare to warn that a source query won't resolve in the target env. Warnings never block — a
/// query with dangling ids is still promotable, it just won't match anything.
/// </summary>
public static class QueryValidator
{
    public static IReadOnlyList<string> Validate(QueryModel model, QueryMetadata meta)
    {
        var warnings = new List<string>();
        if (meta.IsEmpty) return warnings; // no catalog to validate against

        var entityTypes = new HashSet<string>(meta.EntityTypeIds, StringComparer.OrdinalIgnoreCase);
        var fields = new HashSet<string>(meta.AllFieldTypeIds, StringComparer.OrdinalIgnoreCase);
        var linkTypes = new HashSet<string>(meta.LinkTypeIds, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(model.EntityTypeId) && !entityTypes.Contains(model.EntityTypeId))
            warnings.Add($"Entity type '{model.EntityTypeId}' does not exist in this environment.");

        void CheckCriteria(IEnumerable<CriterionModel> criteria, string where)
        {
            foreach (var c in criteria)
                if (!string.IsNullOrWhiteSpace(c.FieldTypeId) && !fields.Contains(c.FieldTypeId))
                    warnings.Add($"Field '{c.FieldTypeId}' ({where}) does not exist in this environment.");
        }

        void Walk(CriteriaGroup? g, string where)
        {
            if (g is null) return;
            CheckCriteria(g.Criteria, where);
            Walk(g.SubQuery, where);
        }

        Walk(model.DataQuery, "data query");

        if (model.LinkQuery is { } lq)
        {
            if (!string.IsNullOrWhiteSpace(lq.LinkTypeId) && !linkTypes.Contains(lq.LinkTypeId))
                warnings.Add($"Link type '{lq.LinkTypeId}' does not exist in this environment.");
            if (!string.IsNullOrWhiteSpace(lq.SourceEntityTypeId) && !entityTypes.Contains(lq.SourceEntityTypeId))
                warnings.Add($"Link source entity type '{lq.SourceEntityTypeId}' does not exist in this environment.");
            if (!string.IsNullOrWhiteSpace(lq.TargetEntityTypeId) && !entityTypes.Contains(lq.TargetEntityTypeId))
                warnings.Add($"Link target entity type '{lq.TargetEntityTypeId}' does not exist in this environment.");
            CheckCriteria(lq.SourceCriteria, "link source");
            CheckCriteria(lq.TargetCriteria, "link target");
        }

        return warnings;
    }
}
