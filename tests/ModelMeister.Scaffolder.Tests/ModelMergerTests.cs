using Shouldly;
using ModelMeister.Scaffolder;
using Xunit;

namespace ModelMeister.Scaffolder.Tests;

public class ModelMergerTests
{
    [Fact]
    public void Disjoint_entity_types_are_concatenated()
    {
        var a = new InriverModelJson { EntityTypes = { new JsonEntityType { Id = "Product" } } };
        var b = new InriverModelJson { EntityTypes = { new JsonEntityType { Id = "Item" } } };
        var (merged, conflicts) = new ModelMerger(MergeConflictPolicy.OverlayWins).Merge(a, b);
        merged.EntityTypes.Select(e => e.Id).ShouldBe(new[] { "Item", "Product" }, ignoreOrder: true);
        conflicts.ShouldBeEmpty();
    }

    [Fact]
    public void Overlay_wins_replaces_base_on_conflict()
    {
        var a = new InriverModelJson { EntityTypes = { new JsonEntityType { Id = "Product", IsLinkEntityType = false } } };
        var b = new InriverModelJson { EntityTypes = { new JsonEntityType { Id = "Product", IsLinkEntityType = true } } };
        var (merged, conflicts) = new ModelMerger(MergeConflictPolicy.OverlayWins).Merge(a, b);
        merged.EntityTypes.Single().IsLinkEntityType.ShouldBeTrue();
        conflicts.ShouldHaveSingleItem();
    }

    [Fact]
    public void Base_wins_keeps_base_on_conflict()
    {
        var a = new InriverModelJson { EntityTypes = { new JsonEntityType { Id = "Product", IsLinkEntityType = false } } };
        var b = new InriverModelJson { EntityTypes = { new JsonEntityType { Id = "Product", IsLinkEntityType = true } } };
        var (merged, _) = new ModelMerger(MergeConflictPolicy.BaseWins).Merge(a, b);
        merged.EntityTypes.Single().IsLinkEntityType.ShouldBeFalse();
    }

    [Fact]
    public void Cvl_values_keyed_by_cvl_id_and_key()
    {
        var a = new InriverModelJson { CvlValues = { new JsonCvlValue { CvlId = "Brand", Key = "Nike" } } };
        var b = new InriverModelJson { CvlValues = { new JsonCvlValue { CvlId = "Brand", Key = "Adidas" }, new JsonCvlValue { CvlId = "Brand", Key = "Nike" } } };
        var (merged, conflicts) = new ModelMerger(MergeConflictPolicy.OverlayWins).Merge(a, b);
        merged.CvlValues.Count.ShouldBe(2);
        conflicts.Count.ShouldBe(1); // Nike conflicts
    }
}
