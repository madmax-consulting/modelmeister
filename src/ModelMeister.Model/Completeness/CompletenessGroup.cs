using ModelMeister.Model.Primitives;

namespace ModelMeister.Model.Completeness;

/// <summary>
/// Base class for a completeness group — the bucket against which per-field completeness rules
/// (<see cref="CompletenessRuleAttribute"/>) accrue weight. Weights across rules contributing to
/// the same (entity, group) pair must sum to 100 (enforced by validator code MM051).
/// </summary>
public abstract class CompletenessGroup
{
    protected CompletenessGroup()
    {
        Name = new LocaleString(NameHumanizer.Humanize(GetType().Name));
    }

    /// <summary>The group's localised display name. Defaults to the CLR type name.</summary>
    public LocaleString Name { get; init; }

    /// <summary>Overall group weight (informational; rule contributions are what's enforced).</summary>
    public virtual int Weight => 0;

    /// <summary>Display sort order in the inriver UI.</summary>
    public virtual int SortOrder => 0;
}
