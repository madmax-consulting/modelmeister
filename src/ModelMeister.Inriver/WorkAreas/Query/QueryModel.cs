namespace ModelMeister.Inriver.WorkAreas.Query;

/// <summary>Boolean join between criteria in a group. Names mirror <c>inRiver.Remoting.Query.Join</c>.</summary>
public enum QJoin { And, Or }

/// <summary>Link traversal direction. Names mirror <c>inRiver.Remoting.Query.LinkDirection</c>.</summary>
public enum QLinkDirection { OutBound, InBound }

/// <summary>Comparison operator. Names (and order) mirror <c>inRiver.Remoting.Query.Operator</c> 1:1 so the
/// mapper can convert by name and JSON round-trips faithfully.</summary>
public enum QOperator
{
    Equal, BeginsWith, Contains, GreaterThanOrEqual, GreaterThan, IsNotNull, IsNull, LessThan,
    LessThanOrEqual, NotEqual, IsTrue, IsFalse, Empty, NotEmpty, ContainsAll, ContainsAny,
    NotContainsAny, NotContainsAll, NotContains,
}

/// <summary>The curated set of <c>SystemQuery</c> scalar fields the builder edits as criteria rows. List and
/// interval system fields (segments, entity-id lists) are preserved across an edit but not surfaced.</summary>
public enum SystemField
{
    EntityTypeId, FieldSetId, CreatedBy, ModifiedBy, LockedBy, Publication, Completeness, Channel,
    EntityId, Created, LastModified,
}

/// <summary>
/// Inriver-free, UI-bindable editing projection of an inriver <c>ComplexQuery</c>. The canonical wire format
/// stays the serialized <c>ComplexQuery</c> (<see cref="WorkAreaFolderDto.QueryJson"/>); this model only
/// exists so the query builder and field-level diff can work in plain terms. Round-trips faithfully for the
/// parts it models (<see cref="EntityTypeId"/>, <see cref="ChannelId"/>, <see cref="DataQuery"/>,
/// <see cref="SystemCriteria"/>, <see cref="LinkQuery"/>); completeness / specification sub-queries are
/// preserved on save but flagged via <see cref="HasUnsupportedParts"/>.
/// </summary>
public sealed class QueryModel
{
    public string? EntityTypeId { get; set; }
    public int? ChannelId { get; set; }
    public CriteriaGroup? DataQuery { get; set; }
    public List<SystemFieldCriterion> SystemCriteria { get; set; } = [];
    public LinkQueryModel? LinkQuery { get; set; }

    /// <summary>True when the source query carried completeness / specification parts the builder cannot
    /// show or edit (they are preserved verbatim when saving via the mapper's <c>preserveFrom</c>).</summary>
    public bool HasUnsupportedParts { get; set; }
}

/// <summary>A group of field criteria joined by <see cref="Join"/>, with an optional nested sub-group.</summary>
public sealed class CriteriaGroup
{
    public QJoin Join { get; set; }
    public List<CriterionModel> Criteria { get; set; } = [];
    public CriteriaGroup? SubQuery { get; set; }
}

/// <summary>One field criterion. <see cref="Value"/> is held as text for editing; the mapper coerces it back
/// to the inriver <c>Criteria.Value</c> object (non-string scalars are best-effort — a known limitation).</summary>
public sealed class CriterionModel
{
    public string FieldTypeId { get; set; } = "";
    public QOperator Operator { get; set; }
    public string? Value { get; set; }
    public bool? Interval { get; set; }
    public string? Language { get; set; }
}

/// <summary>One <c>SystemQuery</c> scalar condition (field + operator + value).</summary>
public sealed class SystemFieldCriterion
{
    public SystemField Field { get; set; }
    public QOperator Operator { get; set; }
    public string? Value { get; set; }
}

/// <summary>Editable projection of a <c>LinkQuery</c> (link traversal + source/target criteria).</summary>
public sealed class LinkQueryModel
{
    public string? LinkTypeId { get; set; }
    public QLinkDirection Direction { get; set; }
    public string? SourceEntityTypeId { get; set; }
    public string? TargetEntityTypeId { get; set; }
    public List<CriterionModel> SourceCriteria { get; set; } = [];
    public List<CriterionModel> TargetCriteria { get; set; } = [];
}
