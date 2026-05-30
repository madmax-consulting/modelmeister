using Shouldly;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Inriver.Statistics;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Blast-radius analysis joins destructive changes to live instance counts. The contract: only
/// changes that touch a <b>populated</b> entity type are reported, and they're ordered heaviest-first.
/// </summary>
public class BlastRadiusTests
{
    private static EntityStatistics Stats(params (string id, int total)[] counts) => new()
    {
        CapturedUtc = DateTime.UtcNow,
        Types = counts.Select(c => new EntityTypeStat(c.id, c.id, c.total, 0, 0)).ToList(),
    };

    private static LiveModel Live(params LiveEntityType[] types) => new()
    {
        EnvironmentUrl = "test",
        CapturedUtc = DateTime.UtcNow,
        EntityTypes = types,
    };

    private static ModelChangeSet ChangeSet(params ModelChange[] changes) => new() { Changes = changes };

    [Fact]
    public void Deleting_a_populated_entity_type_is_flagged()
    {
        var changes = ChangeSet(new DeleteEntityType("Product"));
        var live = Live(new LiveEntityType { Id = "Product", Name = new LocaleString("Product") });

        var entries = BlastRadius.Assess(changes, live, Stats(("Product", 48231)));

        entries.Count.ShouldBe(1);
        entries[0].Kind.ShouldBe(BlastRadiusKind.EntityTypeDelete);
        entries[0].EntityCount.ShouldBe(48231);
        entries[0].Describe().ShouldContain("48,231");
    }

    [Fact]
    public void Deleting_an_empty_entity_type_does_not_cry_wolf()
    {
        var changes = ChangeSet(new DeleteEntityType("Draft"));
        var live = Live(new LiveEntityType { Id = "Draft", Name = new LocaleString("Draft") });

        BlastRadius.Assess(changes, live, Stats(("Draft", 0))).ShouldBeEmpty();
    }

    [Fact]
    public void Deleting_a_field_resolves_its_owner_from_the_live_model()
    {
        var changes = ChangeSet(new DeleteFieldType("ProductWeight"));
        var live = Live(new LiveEntityType
        {
            Id = "Product",
            Name = new LocaleString("Product"),
            Fields = new[] { new LiveFieldType { Id = "ProductWeight", EntityTypeId = "Product", Name = new LocaleString("Weight"), DataType = Datatype.Double } },
        });

        var entries = BlastRadius.Assess(changes, live, Stats(("Product", 1200)));

        entries.Count.ShouldBe(1);
        entries[0].Kind.ShouldBe(BlastRadiusKind.FieldDelete);
        entries[0].EntityTypeId.ShouldBe("Product");
        entries[0].EntityCount.ShouldBe(1200);
    }

    [Fact]
    public void Datatype_change_on_a_populated_type_is_flagged()
    {
        var owner = new LoadedEntityType { ClrType = typeof(object), EntityTypeId = "Product", Name = new LocaleString("Product") };
        var field = new LoadedField { Field = new Model.Field<int>(), Id = "ProductCount", EntityTypeId = "Product", PropertyName = "Count", Name = new LocaleString("Count"), DataType = Datatype.Integer };
        var changes = ChangeSet(new ChangeFieldDatatype(field, owner, Datatype.String, Datatype.Integer));
        var live = Live(new LiveEntityType { Id = "Product", Name = new LocaleString("Product") });

        var entries = BlastRadius.Assess(changes, live, Stats(("Product", 99)));

        entries.Count.ShouldBe(1);
        entries[0].Kind.ShouldBe(BlastRadiusKind.DatatypeChange);
        entries[0].EntityCount.ShouldBe(99);
    }

    [Fact]
    public void Entries_are_ordered_heaviest_first()
    {
        var changes = ChangeSet(new DeleteEntityType("Small"), new DeleteEntityType("Huge"));
        var live = Live(
            new LiveEntityType { Id = "Small", Name = new LocaleString("Small") },
            new LiveEntityType { Id = "Huge", Name = new LocaleString("Huge") });

        var entries = BlastRadius.Assess(changes, live, Stats(("Small", 10), ("Huge", 90000)));

        entries.Select(e => e.EntityTypeId).ShouldBe(new[] { "Huge", "Small" });
        BlastRadius.TotalAtRisk(entries).ShouldBe(90010);
    }

    [Fact]
    public void Non_destructive_changes_are_ignored()
    {
        var et = new LoadedEntityType { ClrType = typeof(object), EntityTypeId = "Product", Name = new LocaleString("Product") };
        var changes = ChangeSet(new AddEntityType(et), new UpdateEntityType(et));
        var live = Live(new LiveEntityType { Id = "Product", Name = new LocaleString("Product") });

        BlastRadius.Assess(changes, live, Stats(("Product", 5000))).ShouldBeEmpty();
    }

    [Fact]
    public void Stats_lookup_is_case_insensitive_and_defaults_to_zero()
    {
        var stats = Stats(("Product", 7));
        stats.CountFor("product").ShouldBe(7);
        stats.CountFor("Unknown").ShouldBe(0);
        stats.TotalEntities.ShouldBe(7);
        EntityStatistics.Empty.TotalEntities.ShouldBe(0);
    }
}
