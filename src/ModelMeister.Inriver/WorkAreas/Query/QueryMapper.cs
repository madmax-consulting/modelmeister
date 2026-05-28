using System.Globalization;
using inRiver.Remoting.Query;
using IrQuery = inRiver.Remoting.Query.Query;

namespace ModelMeister.Inriver.WorkAreas.Query;

/// <summary>
/// Converts between the inriver <see cref="ComplexQuery"/> object graph and the UI-bindable
/// <see cref="QueryModel"/>. <see cref="ToComplexQuery"/> takes an optional <c>preserveFrom</c> so an edit
/// keeps the parts the builder doesn't model — completeness / specification sub-queries and the
/// <c>SystemQuery</c> list / interval fields — rather than dropping them.
/// </summary>
public static class QueryMapper
{
    // ---- enum bridges (names are identical on both sides, so convert by name) ----
    private static QOperator ToQ(Operator op) => Enum.Parse<QOperator>(op.ToString());
    private static Operator ToIr(QOperator op) => Enum.Parse<Operator>(op.ToString());
    private static QJoin ToQ(Join j) => Enum.Parse<QJoin>(j.ToString());
    private static Join ToIr(QJoin j) => Enum.Parse<Join>(j.ToString());
    private static QLinkDirection ToQ(LinkDirection d) => Enum.Parse<QLinkDirection>(d.ToString());
    private static LinkDirection ToIr(QLinkDirection d) => Enum.Parse<LinkDirection>(d.ToString());

    // ---------------- ComplexQuery -> QueryModel ----------------

    public static QueryModel ToModel(ComplexQuery? q)
    {
        if (q is null) return new QueryModel();
        return new QueryModel
        {
            EntityTypeId = q.EntityTypeId,
            ChannelId = q.ChannelId,
            DataQuery = ToModel(q.DataQuery),
            SystemCriteria = ToModel(q.SystemQuery),
            LinkQuery = ToModel(q.LinkQuery),
            HasUnsupportedParts = q.CompletenessQuery is not null || q.SpecificationQuery is not null,
        };
    }

    private static CriteriaGroup? ToModel(IrQuery? q)
    {
        if (q is null) return null;
        return new CriteriaGroup
        {
            Join = ToQ(q.Join),
            Criteria = (q.Criteria ?? []).Select(ToModel).ToList(),
            SubQuery = ToModel(q.SubQuery),
        };
    }

    private static CriterionModel ToModel(Criteria c) => new()
    {
        FieldTypeId = c.FieldTypeId ?? "",
        Operator = ToQ(c.Operator),
        Value = c.Value?.ToString(),
        Interval = c.Interval,
        Language = c.Language,
    };

    private static LinkQueryModel? ToModel(LinkQuery? l)
    {
        if (l is null) return null;
        return new LinkQueryModel
        {
            LinkTypeId = l.LinkTypeId,
            Direction = ToQ(l.Direction),
            SourceEntityTypeId = l.SourceEntityTypeId,
            TargetEntityTypeId = l.TargetEntityTypeId,
            SourceCriteria = (l.SourceCriteria ?? []).Select(ToModel).ToList(),
            TargetCriteria = (l.TargetCriteria ?? []).Select(ToModel).ToList(),
        };
    }

    private static List<SystemFieldCriterion> ToModel(SystemQuery? s)
    {
        var list = new List<SystemFieldCriterion>();
        if (s is null) return list;
        foreach (var (field, def) in SysFields)
        {
            var raw = def.Get(s);
            if (raw is null || (raw is string str && string.IsNullOrEmpty(str))) continue;
            list.Add(new SystemFieldCriterion { Field = field, Operator = ToQ(def.GetOp(s)), Value = Stringify(raw) });
        }
        return list;
    }

    // ---------------- QueryModel -> ComplexQuery ----------------

    /// <summary>Build an inriver <see cref="ComplexQuery"/> from the model. When <paramref name="preserveFrom"/>
    /// is supplied, its completeness / specification sub-queries and its <c>SystemQuery</c> list / interval
    /// fields are carried over so a builder edit never silently drops them.</summary>
    public static ComplexQuery ToComplexQuery(QueryModel model, ComplexQuery? preserveFrom = null)
    {
        var result = new ComplexQuery
        {
            EntityTypeId = string.IsNullOrWhiteSpace(model.EntityTypeId) ? null : model.EntityTypeId,
            ChannelId = model.ChannelId,
            DataQuery = ToIr(model.DataQuery),
            LinkQuery = ToIr(model.LinkQuery),
            CompletenessQuery = preserveFrom?.CompletenessQuery,
            SpecificationQuery = preserveFrom?.SpecificationQuery,
        };

        var sys = ToIr(model.SystemCriteria, preserveFrom?.SystemQuery);
        if (sys is not null) result.SystemQuery = sys;
        return result;
    }

    private static IrQuery? ToIr(CriteriaGroup? g)
    {
        if (g is null) return null;
        return new IrQuery
        {
            Join = ToIr(g.Join),
            Criteria = NullIfEmpty(g.Criteria.Select(ToIr).ToList()),
            SubQuery = ToIr(g.SubQuery),
        };
    }

    // Normalise empty collections to null so a rebuilt query matches inriver's null-omitting serialization
    // (inriver leaves unset criteria lists null rather than emitting an empty array).
    private static List<T>? NullIfEmpty<T>(List<T> list) => list.Count == 0 ? null : list;

    private static Criteria ToIr(CriterionModel m) => new()
    {
        FieldTypeId = m.FieldTypeId,
        Operator = ToIr(m.Operator),
        Value = m.Value,
        Interval = m.Interval,
        Language = m.Language,
    };

    private static LinkQuery? ToIr(LinkQueryModel? l)
    {
        if (l is null) return null;
        return new LinkQuery
        {
            LinkTypeId = l.LinkTypeId,
            Direction = ToIr(l.Direction),
            SourceEntityTypeId = l.SourceEntityTypeId,
            TargetEntityTypeId = l.TargetEntityTypeId,
            SourceCriteria = NullIfEmpty(l.SourceCriteria.Select(ToIr).ToList()),
            TargetCriteria = NullIfEmpty(l.TargetCriteria.Select(ToIr).ToList()),
        };
    }

    private static SystemQuery? ToIr(List<SystemFieldCriterion> criteria, SystemQuery? preserveFrom)
    {
        // Start from the preserved system query so list/interval fields survive; else only build if needed.
        if (criteria.Count == 0 && preserveFrom is null) return null;
        var s = new SystemQuery();
        if (preserveFrom is not null)
        {
            // Carry the fields the builder doesn't model. SegmentIdsOperator has a validating setter
            // (only ContainsAny/NotContainsAny), so only touch it when segments are actually set.
            if (preserveFrom.SegmentIds is { Count: > 0 })
            {
                s.SegmentIds = preserveFrom.SegmentIds;
                s.SegmentIdsOperator = preserveFrom.SegmentIdsOperator;
            }
            if (preserveFrom.EntityIdsList is { Count: > 0 })
                s.EntityIdsList = preserveFrom.EntityIdsList;
            s.IntervalValueCreated = preserveFrom.IntervalValueCreated;
            s.IntervalValueLastModified = preserveFrom.IntervalValueLastModified;
        }
        foreach (var c in criteria)
            if (SysFields.TryGetValue(c.Field, out var def))
                def.Set(s, c.Value, ToIr(c.Operator));
        return s;
    }

    // ---------------- SystemQuery field table (curated scalar fields) ----------------

    private sealed record SysFieldDef(
        Func<SystemQuery, object?> Get, Func<SystemQuery, Operator> GetOp, Action<SystemQuery, string?, Operator> Set);

    private static readonly IReadOnlyDictionary<SystemField, SysFieldDef> SysFields = new Dictionary<SystemField, SysFieldDef>
    {
        [SystemField.EntityTypeId] = new(s => s.EntityTypeId, s => s.EntityTypeIdOperator, (s, v, o) => { s.EntityTypeId = v; s.EntityTypeIdOperator = o; }),
        [SystemField.FieldSetId] = new(s => s.FieldSetId, s => s.FieldSetIdOperator, (s, v, o) => { s.FieldSetId = v; s.FieldSetIdOperator = o; }),
        [SystemField.CreatedBy] = new(s => s.CreatedBy, s => s.CreatedByOperator, (s, v, o) => { s.CreatedBy = v; s.CreatedByOperator = o; }),
        [SystemField.ModifiedBy] = new(s => s.ModifiedBy, s => s.ModifiedByOperator, (s, v, o) => { s.ModifiedBy = v; s.ModifiedByOperator = o; }),
        [SystemField.LockedBy] = new(s => s.LockedBy, s => s.LockedByOperator, (s, v, o) => { s.LockedBy = v; s.LockedByOperator = o; }),
        [SystemField.Publication] = new(s => s.Publication, s => s.PublicationOperator, (s, v, o) => { s.Publication = v; s.PublicationOperator = o; }),
        [SystemField.Completeness] = new(s => s.Completeness, s => s.CompletenessOperator, (s, v, o) => { s.Completeness = ParseInt(v); s.CompletenessOperator = o; }),
        [SystemField.Channel] = new(s => s.Channel, s => s.ChannelOperator, (s, v, o) => { s.Channel = ParseInt(v); s.ChannelOperator = o; }),
        [SystemField.EntityId] = new(s => s.EntityId, s => s.EntityIdOperator, (s, v, o) => { s.EntityId = ParseInt(v); s.EntityIdOperator = o; }),
        [SystemField.Created] = new(s => s.Created, s => s.CreatedOperator, (s, v, o) => { s.Created = ParseDate(v); s.CreatedOperator = o; }),
        [SystemField.LastModified] = new(s => s.LastModified, s => s.LastModifiedOperator, (s, v, o) => { s.LastModified = ParseDate(v); s.LastModifiedOperator = o; }),
    };

    private static string Stringify(object value) => value switch
    {
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static int? ParseInt(string? v) => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    private static DateTime? ParseDate(string? v) => DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;
}
