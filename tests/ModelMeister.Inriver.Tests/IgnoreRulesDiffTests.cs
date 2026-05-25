using Shouldly;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Pins the ignore-differences settings: <see cref="MergePolicy.IgnoredFieldProperties"/> suppresses
/// per-property diffs, and <see cref="MergePolicy.IgnoredFieldIdPatterns"/> suppresses a field type
/// entirely by id (contains / starts-with / ends-with).
/// </summary>
public class IgnoreRulesDiffTests
{
    [Fact]
    public void Ignoring_TrackChanges_property_suppresses_that_diff()
    {
        var code = Code(MakeField("ProductCount", trackChanges: true));
        var live = Live(LiveField("ProductCount", trackChanges: false));

        // Without the ignore the field updates; with it the only difference is suppressed.
        ModelDiffer.Diff(code, live).Of<UpdateFieldType>().ShouldHaveSingleItem();

        var policy = MergePolicy.Default with { IgnoredFieldProperties = ["TrackChanges"] };
        ModelDiffer.Diff(code, live, policy).Of<UpdateFieldType>().ShouldBeEmpty();
    }

    [Fact]
    public void Ignoring_TrackChanges_still_surfaces_other_property_diffs()
    {
        var code = Code(MakeField("ProductCount", trackChanges: true, mandatory: true));
        var live = Live(LiveField("ProductCount", trackChanges: false, mandatory: false));

        // Mandatory still differs, so the update survives even though TrackChanges is ignored.
        var policy = MergePolicy.Default with { IgnoredFieldProperties = ["TrackChanges"] };
        ModelDiffer.Diff(code, live, policy).Of<UpdateFieldType>().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(FieldIdMatchKind.EndsWith, "_internal")]
    [InlineData(FieldIdMatchKind.StartsWith, "Product")]
    [InlineData(FieldIdMatchKind.Contains, "Sku")]
    public void Field_id_rule_suppresses_update(FieldIdMatchKind kind, string value)
    {
        var code = Code(MakeField("ProductSku_internal", trackChanges: true));
        var live = Live(LiveField("ProductSku_internal", trackChanges: false));

        var policy = MergePolicy.Default with { IgnoredFieldIdPatterns = [new FieldIdIgnoreRule(kind, value)] };
        ModelDiffer.Diff(code, live, policy).Of<UpdateFieldType>().ShouldBeEmpty();
    }

    [Fact]
    public void Field_id_rule_suppresses_add()
    {
        // Field exists only in code — normally an add; the id rule suppresses it.
        var code = Code(MakeField("ProductSku_internal", trackChanges: true));
        var live = Live(); // no fields live

        ModelDiffer.Diff(code, live).Of<AddFieldType>().ShouldHaveSingleItem();

        var policy = MergePolicy.Default with { IgnoredFieldIdPatterns = [new FieldIdIgnoreRule(FieldIdMatchKind.EndsWith, "_internal")] };
        ModelDiffer.Diff(code, live, policy).Of<AddFieldType>().ShouldBeEmpty();
    }

    [Fact]
    public void Field_id_rule_suppresses_delete()
    {
        var code = Code(); // no code fields
        var live = Live(LiveField("ProductSku_internal", trackChanges: true));

        var deleteAll = MergePolicy.Default with { AllowDeletes = true };
        ModelDiffer.Diff(code, live, deleteAll).Of<DeleteFieldType>().ShouldHaveSingleItem();

        var policy = deleteAll with { IgnoredFieldIdPatterns = [new FieldIdIgnoreRule(FieldIdMatchKind.Contains, "internal")] };
        ModelDiffer.Diff(code, live, policy).Of<DeleteFieldType>().ShouldBeEmpty();
    }

    [Fact]
    public void Non_matching_id_rule_leaves_diff_intact()
    {
        var code = Code(MakeField("ProductCount", trackChanges: true));
        var live = Live(LiveField("ProductCount", trackChanges: false));

        var policy = MergePolicy.Default with { IgnoredFieldIdPatterns = [new FieldIdIgnoreRule(FieldIdMatchKind.EndsWith, "_internal")] };
        ModelDiffer.Diff(code, live, policy).Of<UpdateFieldType>().ShouldHaveSingleItem();
    }

    // ---------- helpers ----------
    private static LoadedField MakeField(string id, bool trackChanges, bool mandatory = false)
    {
        var field = new Field<int> { TrackChanges = trackChanges, Mandatory = mandatory };
        return new LoadedField
        {
            Field = field,
            Id = id,
            EntityTypeId = "Product",
            PropertyName = id,
            Name = new LocaleString(id),
            DataType = Datatype.Integer,
        };
    }

    private static LiveFieldType LiveField(string id, bool trackChanges, bool mandatory = false) => new()
    {
        Id = id,
        EntityTypeId = "Product",
        Name = new LocaleString(id),
        DataType = Datatype.Integer,
        TrackChanges = trackChanges,
        Mandatory = mandatory,
    };

    private static LoadedModel Code(params LoadedField[] fields) => new()
    {
        EntityTypes = new[]
        {
            new LoadedEntityType
            {
                ClrType = typeof(object),
                EntityTypeId = "Product",
                Name = new LocaleString("Product"),
                Fields = fields.ToList(),
            },
        },
    };

    private static LiveModel Live(params LiveFieldType[] fields) => new()
    {
        EnvironmentUrl = "test",
        CapturedUtc = System.DateTime.UtcNow,
        EntityTypes = new[]
        {
            new LiveEntityType
            {
                Id = "Product",
                Name = new LocaleString("Product"),
                Fields = fields,
            },
        },
    };
}
