using Shouldly;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Per-kind Index/sort-order gating. The default policy ignores Index differences on update for every
/// concept; each kind (field / category / link type) has its own opt-in flag so enabling one does not
/// drag in the others. Pins the fix for "changed a link index in code, loaded, saw no diff".
/// </summary>
public class IndexSortingPolicyDiffTests
{
    private static LiveModel Live(params LiveLinkType[] links) => new()
    {
        EnvironmentUrl = "test",
        CapturedUtc = DateTime.UtcNow,
        LinkTypes = links,
    };

    // ---------- Link types ----------

    private static (LoadedModel code, LiveModel live) LinkIndexMismatch()
    {
        var code = new LoadedModel
        {
            LinkTypes = new[]
            {
                new LoadedLinkType
                {
                    ClrType = typeof(object),
                    LinkTypeId = "ProductSupplier",
                    SourceEntityTypeId = "Product",
                    TargetEntityTypeId = "Supplier",
                    Index = 5,
                },
            },
        };
        var live = Live(new LiveLinkType
        {
            Id = "ProductSupplier",
            SourceEntityTypeId = "Product",
            TargetEntityTypeId = "Supplier",
            Index = 1,
        });
        return (code, live);
    }

    [Fact]
    public void Link_index_only_change_is_ignored_by_default()
    {
        var (code, live) = LinkIndexMismatch();
        ModelDiffer.Diff(code, live).Of<UpdateLinkType>().ShouldBeEmpty();
    }

    [Fact]
    public void Link_index_only_change_surfaces_when_opted_in()
    {
        var (code, live) = LinkIndexMismatch();
        var policy = MergePolicy.Default with { IgnoreLinkTypeIndexSortingOnUpdate = false };
        ModelDiffer.Diff(code, live, policy).Of<UpdateLinkType>().Count().ShouldBe(1);
    }

    [Fact]
    public void Enabling_category_index_does_not_surface_link_index()
    {
        var (code, live) = LinkIndexMismatch();
        // Opting into category index must not drag in the link-type index.
        var policy = MergePolicy.Default with { IgnoreCategoryIndexSortingOnUpdate = false };
        ModelDiffer.Diff(code, live, policy).Of<UpdateLinkType>().ShouldBeEmpty();
    }

    // ---------- Categories ----------

    private static (LoadedModel code, LiveModel live) CategoryIndexMismatch()
    {
        var code = new LoadedModel
        {
            Categories = new[]
            {
                new LoadedCategory { ClrType = typeof(object), CategoryId = "Marketing", Name = new LocaleString("Marketing"), Index = 9 },
            },
        };
        var live = new LiveModel
        {
            EnvironmentUrl = "test",
            CapturedUtc = DateTime.UtcNow,
            Categories = new[] { new LiveCategory { Id = "Marketing", Name = new LocaleString("Marketing"), Index = 2 } },
        };
        return (code, live);
    }

    [Fact]
    public void Category_index_only_change_is_ignored_by_default()
    {
        var (code, live) = CategoryIndexMismatch();
        ModelDiffer.Diff(code, live).Of<UpdateCategory>().ShouldBeEmpty();
    }

    [Fact]
    public void Category_index_only_change_surfaces_when_opted_in()
    {
        var (code, live) = CategoryIndexMismatch();
        var policy = MergePolicy.Default with { IgnoreCategoryIndexSortingOnUpdate = false };
        ModelDiffer.Diff(code, live, policy).Of<UpdateCategory>().Count().ShouldBe(1);
    }

    // ---------- Fields ----------

    private static (LoadedModel code, LiveModel live) FieldIndexMismatch()
    {
        var field = new Model.Field<int> { Index = 7 };
        var owner = new LoadedEntityType
        {
            ClrType = typeof(object),
            EntityTypeId = "Product",
            Name = new LocaleString("Product"),
            Fields = new List<LoadedField>
            {
                new()
                {
                    Field = field,
                    Id = "ProductCount",
                    EntityTypeId = "Product",
                    PropertyName = "Count",
                    Name = new LocaleString("Count"),
                    DataType = Datatype.Integer,
                },
            },
        };
        var code = new LoadedModel { EntityTypes = new[] { owner } };
        var live = new LiveModel
        {
            EnvironmentUrl = "test",
            CapturedUtc = DateTime.UtcNow,
            EntityTypes = new[]
            {
                new LiveEntityType
                {
                    Id = "Product",
                    Name = new LocaleString("Product"),
                    Fields = new[]
                    {
                        new LiveFieldType { Id = "ProductCount", EntityTypeId = "Product", Name = new LocaleString("Count"), DataType = Datatype.Integer, Index = 1 },
                    },
                },
            },
        };
        return (code, live);
    }

    [Fact]
    public void Field_index_only_change_is_ignored_by_default()
    {
        var (code, live) = FieldIndexMismatch();
        ModelDiffer.Diff(code, live).Of<UpdateFieldType>().ShouldBeEmpty();
    }

    [Fact]
    public void Field_index_only_change_surfaces_when_opted_in()
    {
        var (code, live) = FieldIndexMismatch();
        var policy = MergePolicy.Default with { IgnoreFieldIndexSortingOnUpdate = false };
        ModelDiffer.Diff(code, live, policy).Of<UpdateFieldType>().Count().ShouldBe(1);
    }

    [Fact]
    public void Field_index_opt_in_still_honours_per_property_ignore()
    {
        var (code, live) = FieldIndexMismatch();
        // Index opted in via the sort flag, but the per-property ignore list still wins.
        var policy = MergePolicy.Default with
        {
            IgnoreFieldIndexSortingOnUpdate = false,
            IgnoredFieldProperties = new[] { "Index" },
        };
        ModelDiffer.Diff(code, live, policy).Of<UpdateFieldType>().ShouldBeEmpty();
    }
}
