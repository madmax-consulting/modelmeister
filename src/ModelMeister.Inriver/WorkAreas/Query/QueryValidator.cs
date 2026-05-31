namespace ModelMeister.Inriver.WorkAreas.Query;

/// <summary>
/// Flags problems in a query: references (entity types, field types, link types) that don't exist in a given
/// environment's <see cref="QueryMetadata"/>, plus structural / datatype mistakes (a value-requiring operator
/// with no value, <c>IsTrue</c>/<c>IsFalse</c> on a non-boolean field, a non-numeric value on a numeric
/// field). Used (a) live in the builder against the connected env, and (b) in Compare to warn that a source
/// query won't resolve in the target env. Warnings never block — a query with dangling ids is still
/// promotable, it just won't match anything.
/// </summary>
public static class QueryValidator
{
    // Operators that need a value to mean anything.
    private static readonly HashSet<QOperator> ValueRequired =
    [
        QOperator.Equal, QOperator.NotEqual, QOperator.BeginsWith, QOperator.Contains, QOperator.NotContains,
        QOperator.GreaterThan, QOperator.GreaterThanOrEqual, QOperator.LessThan, QOperator.LessThanOrEqual,
        QOperator.ContainsAll, QOperator.ContainsAny, QOperator.NotContainsAny, QOperator.NotContainsAll,
    ];

    // Operators valid only on a boolean field.
    private static readonly HashSet<QOperator> BoolOnly = [QOperator.IsTrue, QOperator.IsFalse];

    public static IReadOnlyList<string> Validate(QueryModel model, QueryMetadata meta)
    {
        var warnings = new List<string>();

        // Structural checks run even without a catalog (they don't need model ids).
        void CheckStructure(IEnumerable<CriterionModel> criteria, string where)
        {
            foreach (var c in criteria)
            {
                if (string.IsNullOrWhiteSpace(c.FieldTypeId)) continue;
                if (ValueRequired.Contains(c.Operator) && string.IsNullOrEmpty(c.Value))
                    warnings.Add($"Field '{c.FieldTypeId}' ({where}) uses {c.Operator} but has no value.");

                var dt = meta.DataTypeOf(c.FieldTypeId);
                if (dt is not null)
                {
                    var isBool = string.Equals(dt, "Boolean", StringComparison.OrdinalIgnoreCase);
                    if (BoolOnly.Contains(c.Operator) && !isBool)
                        warnings.Add($"Field '{c.FieldTypeId}' ({where}) is {dt}, so {c.Operator} does not apply.");
                    if (IsNumeric(dt) && ValueRequired.Contains(c.Operator)
                        && !string.IsNullOrEmpty(c.Value) && !IsNumericValue(c.Value))
                        warnings.Add($"Field '{c.FieldTypeId}' ({where}) is {dt} but the value '{c.Value}' is not numeric.");
                }
            }
        }

        void WalkStructure(CriteriaGroup? g, string where)
        {
            while (g is not null) { CheckStructure(g.Criteria, where); g = g.SubQuery; }
        }

        // Reload-fidelity check (DESIGN F2): the builder edits an n-ary group tree but the wire format is
        // inriver's single-SubQuery chain, and the load path reads exactly one child per level. A group that
        // BOTH carries its own criteria AND has a nested sub-group, or any nesting beyond one level, left-nests
        // on save and would reload reshaped — warn instead of silently changing the boolean structure.
        void CheckGroupShape(CriteriaGroup? g, int depth)
        {
            if (g?.SubQuery is null) return;
            if (g.Criteria.Count > 0)
                warnings.Add("A group has both its own criteria and a nested sub-group — on save the sub-group folds into a serial chain and may reload with a different shape. Move the criteria into their own sub-group to keep it stable.");
            if (depth >= 1)
                warnings.Add("Sub-groups are nested more than one level deep — the editor stores a single nested level, so deeper groups left-nest and may reload reshaped.");
            CheckGroupShape(g.SubQuery, depth + 1);
        }

        WalkStructure(model.DataQuery, "data query");
        CheckGroupShape(model.DataQuery, 0);
        if (model.LinkQuery is { } slq)
        {
            CheckStructure(slq.SourceCriteria, "link source");
            CheckStructure(slq.TargetCriteria, "link target");
        }

        if (meta.IsEmpty) return warnings; // no catalog to check ids against

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

    private static bool IsNumeric(string dataType) =>
        string.Equals(dataType, "Integer", StringComparison.OrdinalIgnoreCase)
        || string.Equals(dataType, "Double", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumericValue(string value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _);
}
