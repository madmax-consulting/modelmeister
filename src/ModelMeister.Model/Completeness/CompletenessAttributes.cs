namespace ModelMeister.Model.Completeness;

/// <summary>
/// Base for completeness-rule attributes. Each rule contributes <see cref="Weight"/> points to its
/// <see cref="Group"/>. Per-(entity, group) weight totals must sum to 100 (validator MM051).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class CompletenessRuleAttribute(int weight, Type group) : Attribute
{
    public int Weight { get; } = weight;
    public Type Group { get; } = group;
    public int Index { get; init; }
    public string? Name { get; init; }

    /// <summary>Which built-in rule kind this attribute represents — drives the inriver mapping.</summary>
    public abstract CompletenessRuleKind Kind { get; }
}

/// <summary>Field is considered complete when it has any non-empty value.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class FieldNotEmptyAttribute(int weight, Type group, string? note = null)
    : CompletenessRuleAttribute(weight, group)
{
    public string? Note { get; } = note;
    public override CompletenessRuleKind Kind => CompletenessRuleKind.FieldNotEmpty;
}

/// <summary>Field is considered complete when its value contains <see cref="Value"/>.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class ContainsValueAttribute(int weight, Type group, string value)
    : CompletenessRuleAttribute(weight, group)
{
    public string Value { get; } = value;
    public override CompletenessRuleKind Kind => CompletenessRuleKind.ContainsValue;
}

/// <summary>Field is considered complete when its value exactly matches <see cref="Expected"/>.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class ExactMatchAttribute(int weight, Type group, string expected)
    : CompletenessRuleAttribute(weight, group)
{
    public string Expected { get; } = expected;
    public override CompletenessRuleKind Kind => CompletenessRuleKind.ExactMatch;
}

/// <summary>Field is considered complete when at least one link of type <see cref="LinkType"/> exists.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class LinkTypeExistsAttribute(int weight, Type group, Type linkType)
    : CompletenessRuleAttribute(weight, group)
{
    public Type LinkType { get; } = linkType;
    public override CompletenessRuleKind Kind => CompletenessRuleKind.LinkTypeExists;
}

/// <summary>Field is considered complete when all related entities themselves report complete.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class RelationsCompleteAttribute(int weight, Type group)
    : CompletenessRuleAttribute(weight, group)
{
    public override CompletenessRuleKind Kind => CompletenessRuleKind.RelationsComplete;
}

/// <summary>Numeric comparison operators recognised by <see cref="NumberEvaluationAttribute"/>.</summary>
public enum NumberEvaluationOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
}

/// <summary>Field is considered complete when its numeric value satisfies the comparison.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class NumberEvaluationAttribute(int weight, Type group, NumberEvaluationOperator op, double value)
    : CompletenessRuleAttribute(weight, group)
{
    public NumberEvaluationOperator Operator { get; } = op;
    public double Value { get; } = value;
    public override CompletenessRuleKind Kind => CompletenessRuleKind.NumberEvaluation;
}
