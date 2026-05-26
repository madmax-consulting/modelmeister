using Shouldly;
using ModelMeister.Model;
using ModelMeister.Model.Loading;
using Xunit;

namespace ModelMeister.Model.Tests;

/// <summary>
/// TrackChanges is on by default — the code model is authoritative. The loader stamps
/// <c>true</c> when a field leaves it unset; an explicit <c>TrackChanges = false</c> initializer
/// opts out. See <see cref="ModelLoader"/> and CLAUDE.md (TrackChanges is not read-through).
/// </summary>
public class TrackChangesDefaultTests
{
    public sealed class TrackEntity : EntityType
    {
        public Field<string> Plain { get; init; } = new();
        [ReadOnlyField] public Field<string> ReadOnlyPlain { get; init; } = new();
        public Field<string> OptedOut { get; init; } = new() { TrackChanges = false };
        [TrackChanges] public Field<string> Attributed { get; init; } = new();
    }

    private static LoadedField Load(string prop) =>
        ModelLoader.LoadFromAssembly(typeof(TrackEntity).Assembly)
            .EntityTypes.Single(e => e.ClrType == typeof(TrackEntity))
            .Fields.Single(f => f.PropertyName == prop);

    [Fact] public void Unset_TrackChanges_defaults_to_true() =>
        Load("Plain").Field.TrackChanges.ShouldBe(true);

    [Fact] public void ReadOnly_field_still_defaults_TrackChanges_to_true() =>
        Load("ReadOnlyPlain").Field.TrackChanges.ShouldBe(true);

    [Fact] public void Explicit_false_TrackChanges_is_preserved() =>
        Load("OptedOut").Field.TrackChanges.ShouldBe(false);

    [Fact] public void TrackChanges_attribute_yields_true() =>
        Load("Attributed").Field.TrackChanges.ShouldBe(true);
}
