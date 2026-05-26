using Shouldly;
using ModelMeister.Model.Completeness;
using Xunit;

namespace ModelMeister.Model.Tests;

/// <summary>
/// Pins the inriver completeness vocabulary. These constants are the one place reconciled against a real
/// environment (see <see cref="CompletenessRuleVocabulary"/> remarks) — a change here should be deliberate.
/// </summary>
public class CompletenessRuleVocabularyTests
{
    [Theory]
    [InlineData(CompletenessRuleKind.FieldNotEmpty, "FieldNotEmpty")]
    [InlineData(CompletenessRuleKind.ContainsValue, "FieldContainsValue")]
    [InlineData(CompletenessRuleKind.ExactMatch, "FieldExactValue")]
    [InlineData(CompletenessRuleKind.LinkTypeExists, "LinkTypeExists")]
    [InlineData(CompletenessRuleKind.RelationsComplete, "RelationsComplete")]
    [InlineData(CompletenessRuleKind.NumberEvaluation, "FieldNumberEvaluation")]
    public void InriverType_is_pinned(CompletenessRuleKind kind, string expected) =>
        CompletenessRuleVocabulary.InriverType(kind).ShouldBe(expected);

    [Theory]
    [InlineData(CompletenessRuleKind.FieldNotEmpty)]
    [InlineData(CompletenessRuleKind.ContainsValue)]
    [InlineData(CompletenessRuleKind.ExactMatch)]
    [InlineData(CompletenessRuleKind.LinkTypeExists)]
    [InlineData(CompletenessRuleKind.RelationsComplete)]
    [InlineData(CompletenessRuleKind.NumberEvaluation)]
    public void Kind_round_trips_through_type_string(CompletenessRuleKind kind)
    {
        CompletenessRuleVocabulary.TryKind(CompletenessRuleVocabulary.InriverType(kind), out var back).ShouldBeTrue();
        back.ShouldBe(kind);
    }

    [Fact]
    public void Unknown_type_does_not_resolve() =>
        CompletenessRuleVocabulary.TryKind("NotARealRuleType", out _).ShouldBeFalse();
}
