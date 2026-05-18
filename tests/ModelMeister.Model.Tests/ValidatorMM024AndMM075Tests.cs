using Shouldly;
using ModelMeister.Model;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;
using ModelMeister.Model.Validation;
using Xunit;

namespace ModelMeister.Model.Tests;

/// <summary>
/// Tests for the two validators added in the review-fix pass: MM024 (CVL DataType compatibility) and
/// MM075 (PerMarket without an IMarketResolver / MarketsCvl).
/// </summary>
public class ValidatorMM024AndMM075Tests
{
    private sealed class StringCvl : Cvl
    {
        public override CvlDataType DataType => CvlDataType.String;
        public override IEnumerable<CvlValue> GetValues() => Array.Empty<CvlValue>();
    }

    private sealed class DoubleCvl : Cvl
    {
        public override CvlDataType DataType => CvlDataType.Double;
        public override IEnumerable<CvlValue> GetValues() => Array.Empty<CvlValue>();
    }

    [Fact]
    public void MM024_field_double_against_string_cvl_errors()
    {
        var loadedCvl = new LoadedCvl
        {
            ClrType = typeof(StringCvl),
            CvlId = "StringCvl",
            DataType = CvlDataType.String,
        };
        var field = new Field<double, StringCvl>();
        var lf = new LoadedField
        {
            Field = field,
            Id = "ProductBadCvl",
            EntityTypeId = "Product",
            PropertyName = "BadCvl",
            Name = new LocaleString("BadCvl"),
            DataType = Datatype.Double,
        };
        var entity = new LoadedEntityType
        {
            ClrType = typeof(object),
            EntityTypeId = "Product",
            Name = new LocaleString("Product"),
            Fields = new() { lf },
        };
        var model = new LoadedModel { EntityTypes = new[] { entity }, Cvls = new[] { loadedCvl } };

        var r = ModelValidator.Validate(model);
        r.Issues.Any(i => i.Code == "MM024").ShouldBeTrue();
    }

    [Fact]
    public void MM024_field_CvlKey_against_string_cvl_is_fine()
    {
        var loadedCvl = new LoadedCvl
        {
            ClrType = typeof(StringCvl),
            CvlId = "StringCvl",
            DataType = CvlDataType.String,
        };
        var field = new Field<CvlKey, StringCvl>();
        var lf = new LoadedField
        {
            Field = field,
            Id = "ProductCvlKey",
            EntityTypeId = "Product",
            PropertyName = "CvlKey",
            Name = new LocaleString("CvlKey"),
            DataType = Datatype.Cvl,
        };
        var entity = new LoadedEntityType
        {
            ClrType = typeof(object),
            EntityTypeId = "Product",
            Name = new LocaleString("Product"),
            Fields = new() { lf },
        };
        var model = new LoadedModel { EntityTypes = new[] { entity }, Cvls = new[] { loadedCvl } };

        var r = ModelValidator.Validate(model);
        r.Issues.Any(i => i.Code == "MM024").ShouldBeFalse();
    }

    [Fact]
    public void MM024_field_double_against_double_cvl_is_fine()
    {
        var loadedCvl = new LoadedCvl
        {
            ClrType = typeof(DoubleCvl),
            CvlId = "DoubleCvl",
            DataType = CvlDataType.Double,
        };
        var field = new Field<double, DoubleCvl>();
        var lf = new LoadedField
        {
            Field = field,
            Id = "ProductDoubleCvl",
            EntityTypeId = "Product",
            PropertyName = "DoubleCvl",
            Name = new LocaleString("DoubleCvl"),
            DataType = Datatype.Double,
        };
        var entity = new LoadedEntityType
        {
            ClrType = typeof(object),
            EntityTypeId = "Product",
            Name = new LocaleString("Product"),
            Fields = new() { lf },
        };
        var model = new LoadedModel { EntityTypes = new[] { entity }, Cvls = new[] { loadedCvl } };

        var r = ModelValidator.Validate(model);
        r.Issues.Any(i => i.Code == "MM024").ShouldBeFalse();
    }

    [Fact]
    public void MM075_PerMarket_without_resolver_or_MarketsCvl_errors()
    {
        // No MarketsCvl is registered in this LoadedModel and no IMarketResolver subclass
        // exists in the current test assembly other than the framework-provided CvlMarketResolver
        // (which doesn't count per the validator's rule).
        var field = new Field<int> { PerMarket = true };
        var lf = new LoadedField
        {
            Field = field,
            Id = "ProductPerMarketCount",
            EntityTypeId = "Product",
            PropertyName = "PerMarketCount",
            Name = new LocaleString("PerMarketCount"),
            DataType = Datatype.Integer,
        };
        var entity = new LoadedEntityType
        {
            ClrType = typeof(object),
            EntityTypeId = "Product",
            Name = new LocaleString("Product"),
            Fields = new() { lf },
        };
        var model = new LoadedModel { EntityTypes = new[] { entity } };

        var r = ModelValidator.Validate(model);
        // Note: this test assumes no other test fixture leaks a MarketsCvl subclass into the
        // AppDomain. If one does in the future, scope this to an isolated AppDomain.
        r.Issues.Any(i => i.Code == "MM075").ShouldBeTrue();
    }

    [Fact]
    public void MM051_includes_contributor_field_ids_and_weights()
    {
        // Mock: only the message format is under test. We don't need a real CompletenessGroup here.
        // Direct call: synthesise weights inside a model.
        // Build a real model with two fields contributing 40 and 50 to the same Group.
        var groupType = typeof(SampleGroup);
        var loadedGroup = new LoadedCompletenessGroup
        {
            ClrType = groupType,
            Name = new LocaleString("Sample"),
            Weight = 100,
            SortOrder = 0,
        };

        var f1 = new Field<int>();
        var f2 = new Field<int>();
        var lf1 = new LoadedField
        {
            Field = f1, Id = "ProductA", EntityTypeId = "Product", PropertyName = "A",
            Name = new LocaleString("A"), DataType = Datatype.Integer,
            Attributes = new[] { (Attribute)new ModelMeister.Model.Completeness.FieldNotEmptyAttribute(40, groupType) },
        };
        var lf2 = new LoadedField
        {
            Field = f2, Id = "ProductB", EntityTypeId = "Product", PropertyName = "B",
            Name = new LocaleString("B"), DataType = Datatype.Integer,
            Attributes = new[] { (Attribute)new ModelMeister.Model.Completeness.FieldNotEmptyAttribute(50, groupType) },
        };
        var entity = new LoadedEntityType
        {
            ClrType = typeof(object),
            EntityTypeId = "Product",
            Name = new LocaleString("Product"),
            Fields = new() { lf1, lf2 },
        };
        var model = new LoadedModel { EntityTypes = new[] { entity }, CompletenessGroups = new[] { loadedGroup } };

        var r = ModelValidator.Validate(model);
        var mm051 = r.Issues.FirstOrDefault(i => i.Code == "MM051");
        mm051.ShouldNotBeNull();
        mm051!.Message.ShouldContain("ProductA=40");
        mm051.Message.ShouldContain("ProductB=50");
    }

    private sealed class SampleGroup : ModelMeister.Model.Completeness.CompletenessGroup
    {
        // Name is set in the base; weight/sort default to 0.
    }
}
