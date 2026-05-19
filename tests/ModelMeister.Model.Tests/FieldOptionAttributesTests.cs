using Shouldly;
using ModelMeister.Model;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;
using ModelMeister.Model.Validation;
using Xunit;

namespace ModelMeister.Model.Tests;

/// <summary>
/// Pin the field-flag attribute surface (<see cref="MandatoryAttribute"/> and friends). Each
/// attribute must stamp the matching <see cref="Field"/> property when the loader runs, and the
/// validator must surface MM012 when both attribute and object initializer set the same flag.
/// </summary>
public class FieldOptionAttributesTests
{
    public sealed class TestFieldset : Fieldset
    {
        public TestFieldset() { Name = new LocaleString("Test"); }
        public override Type EntityType => typeof(AttrEntity);
    }

    public sealed class AltFieldset : Fieldset
    {
        public AltFieldset() { Name = new LocaleString("Alt"); }
        public override Type EntityType => typeof(AttrEntity);
    }

    public sealed class TestCategory : Category
    {
        public TestCategory() { Name = new LocaleString("Test"); }
        public override int Index => 1;
    }

    public sealed class AttrEntity : EntityType
    {
        [Mandatory] public Field<string> Mand { get; init; } = new();
        [Unique] public Field<string> Uniq { get; init; } = new();
        [ReadOnlyField] public Field<string> Ro { get; init; } = new();
        [Hidden] public Field<string> Hid { get; init; } = new();
        [MultiValue] public Field<string> Mv { get; init; } = new();
        [PerMarket] public Field<string> Pm { get; init; } = new();
        [SupportsExpression] public Field<string> Se { get; init; } = new();
        [ShowInEntityOverview] public Field<string> Show { get; init; } = new();
        [IgnoreFieldInEpiserverExport] public Field<string> NoExport { get; init; } = new();
        [TrackChanges] public Field<string> Tracked { get; init; } = new();
        [ExcludeFromDefaultView] public Field<string> Excluded { get; init; } = new();
        [Index(5)] public Field<string> Indexed { get; init; } = new();
        [NumberOfRows(8)] public Field<string> WithRows { get; init; } = new();
        [RegExp(@"^X.*$")] public Field<string> Pattern { get; init; } = new();
        [FieldCategory(typeof(TestCategory))] public Field<string> Categorised { get; init; } = new();
        [Fieldset(typeof(TestFieldset))] public Field<string> Singleset { get; init; } = new();
        [Fieldset(typeof(TestFieldset)), Fieldset(typeof(AltFieldset))] public Field<string> Multiset { get; init; } = new();
    }

    private LoadedField Load(string propertyName) =>
        ModelLoader.LoadFromAssembly(typeof(AttrEntity).Assembly)
            .EntityTypes.Single(e => e.ClrType == typeof(AttrEntity))
            .Fields.Single(f => f.PropertyName == propertyName);

    [Fact] public void Mandatory_attribute_sets_field()       => Load("Mand").Field.Mandatory.ShouldBeTrue();
    [Fact] public void Unique_attribute_sets_field()          => Load("Uniq").Field.Unique.ShouldBeTrue();
    [Fact] public void ReadOnlyField_attribute_sets_field()   => Load("Ro").Field.ReadOnly.ShouldBeTrue();
    [Fact] public void Hidden_attribute_sets_field()          => Load("Hid").Field.Hidden.ShouldBeTrue();
    [Fact] public void MultiValue_attribute_sets_field()      => Load("Mv").Field.MultiValue.ShouldBeTrue();
    [Fact] public void PerMarket_attribute_sets_field()       => Load("Pm").Field.PerMarket.ShouldBeTrue();
    [Fact] public void SupportsExpression_attribute_sets_field() => Load("Se").Field.SupportsExpression.ShouldBeTrue();
    [Fact] public void ShowInEntityOverview_attribute_sets_field() => Load("Show").Field.ShowInEntityOverview.ShouldBeTrue();
    [Fact] public void IgnoreFieldInEpiserverExport_attribute_sets_field() => Load("NoExport").Field.IgnoreFieldInEpiserverExport.ShouldBeTrue();
    [Fact] public void TrackChanges_attribute_sets_field()    => Load("Tracked").Field.TrackChanges.ShouldBe(true);
    [Fact] public void ExcludeFromDefaultView_attribute_sets_field() => Load("Excluded").Field.ExcludeFromDefaultView.ShouldBe(true);
    [Fact] public void Index_attribute_sets_field()           => Load("Indexed").Field.Index.ShouldBe(5);
    [Fact] public void NumberOfRows_attribute_sets_field()    => Load("WithRows").Field.NumberOfRows.ShouldBe(8);
    [Fact] public void RegExp_attribute_sets_field()          => Load("Pattern").Field.RegExp.ShouldBe(@"^X.*$");
    [Fact] public void FieldCategory_attribute_sets_field()   => Load("Categorised").Field.Category.ShouldBe(typeof(TestCategory));

    [Fact]
    public void Fieldset_attribute_sets_field()
    {
        Load("Singleset").Field.Fieldsets.ShouldBe(new[] { typeof(TestFieldset) });
    }

    [Fact]
    public void Multiple_Fieldset_attributes_stack()
    {
        Load("Multiset").Field.Fieldsets.ShouldBe(
            new[] { typeof(TestFieldset), typeof(AltFieldset) }, ignoreOrder: true);
    }

    // --- MM012: duplicate-flag detection ---

    public sealed class DupEntity : EntityType
    {
        [Mandatory] public Field<string> DupBool { get; init; } = new() { Mandatory = true };
        [Index(3)]  public Field<string> DupIndex { get; init; } = new() { Index = 10 };
        [TrackChanges] public Field<string> DupNullable { get; init; } = new() { TrackChanges = true };
        [Mandatory] public Field<string> OnlyAttr { get; init; } = new();
    }

    [Fact]
    public void MM012_fires_when_attribute_and_initializer_both_set_bool()
    {
        var loaded = ModelLoader.LoadFromAssembly(typeof(DupEntity).Assembly);
        var dup = loaded.EntityTypes.Single(e => e.ClrType == typeof(DupEntity));
        var field = dup.Fields.Single(f => f.PropertyName == "DupBool");
        field.DuplicateAttributeFlags.ShouldContain(nameof(Field.Mandatory));

        var issues = ModelValidator.Validate(loaded).Issues.Where(i => i.Code == "MM012").ToList();
        issues.ShouldNotBeEmpty();
        issues.ShouldContain(i => i.Message.Contains("DupBool") && i.Message.Contains("Mandatory"));
    }

    [Fact]
    public void MM012_fires_for_scalar_attribute_when_initializer_set()
    {
        var loaded = ModelLoader.LoadFromAssembly(typeof(DupEntity).Assembly);
        var issues = ModelValidator.Validate(loaded).Issues.Where(i => i.Code == "MM012").ToList();
        issues.ShouldContain(i => i.Message.Contains("DupIndex") && i.Message.Contains("Index"));
    }

    [Fact]
    public void MM012_fires_for_nullable_bool_when_initializer_set_true()
    {
        var loaded = ModelLoader.LoadFromAssembly(typeof(DupEntity).Assembly);
        var issues = ModelValidator.Validate(loaded).Issues.Where(i => i.Code == "MM012").ToList();
        issues.ShouldContain(i => i.Message.Contains("DupNullable") && i.Message.Contains("TrackChanges"));
    }

    [Fact]
    public void MM012_does_not_fire_when_only_attribute_set()
    {
        var loaded = ModelLoader.LoadFromAssembly(typeof(DupEntity).Assembly);
        var dup = loaded.EntityTypes.Single(e => e.ClrType == typeof(DupEntity));
        var field = dup.Fields.Single(f => f.PropertyName == "OnlyAttr");
        field.DuplicateAttributeFlags.ShouldBeEmpty();
    }

    [Fact]
    public void Attribute_value_wins_when_initializer_also_sets_scalar()
    {
        var loaded = ModelLoader.LoadFromAssembly(typeof(DupEntity).Assembly);
        var dup = loaded.EntityTypes.Single(e => e.ClrType == typeof(DupEntity));
        var field = dup.Fields.Single(f => f.PropertyName == "DupIndex");
        field.Field.Index.ShouldBe(3, "the [Index(3)] attribute must overwrite the initializer's Index = 10");
    }
}
