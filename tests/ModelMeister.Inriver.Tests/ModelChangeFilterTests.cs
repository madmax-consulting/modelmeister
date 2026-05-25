using Shouldly;
using ModelMeister.Inriver.Diff;
using ModelMeister.Model;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Pins <see cref="ModelChangeFilter"/>: env→env promotion narrows a full change set to the
/// selected concept(s), bundling an entity type's fields and a CVL's values with their parent.
/// </summary>
public class ModelChangeFilterTests
{
    private static LoadedEntityType Entity(string id) => new()
    {
        ClrType = typeof(object),
        EntityTypeId = id,
        Name = new LocaleString(id),
        Fields = new System.Collections.Generic.List<LoadedField>(),
    };

    private static LoadedField Field(string entityId, string fieldId) => new()
    {
        Field = new Field<int>(),
        Id = fieldId,
        EntityTypeId = entityId,
        PropertyName = fieldId,
        Name = new LocaleString(fieldId),
        DataType = Datatype.Integer,
    };

    private static LoadedCvl Cvl(string id) => new()
    {
        ClrType = typeof(object),
        CvlId = id,
        DataType = CvlDataType.String,
    };

    [Fact]
    public void EntityType_scope_bundles_its_field_changes()
    {
        var product = Entity("Product");
        var other = Entity("Other");
        var set = new ModelChangeSet
        {
            Changes =
            [
                new AddEntityType(product),
                new AddFieldType(Field("Product", "ProductName"), product),
                new UpdateFieldType(Field("Product", "ProductSku"), product),
                new AddEntityType(other),
                new AddFieldType(Field("Other", "OtherName"), other),
            ],
        };

        var result = ModelChangeFilter.ForConcept(set, new PromoteScope(PromoteConcept.EntityType, "Product"));

        result.Changes.Count.ShouldBe(3); // entity + its two fields
        result.Of<AddEntityType>().ShouldHaveSingleItem().EntityType.EntityTypeId.ShouldBe("Product");
        result.Of<AddFieldType>().ShouldNotContain(a => a.Field.Id == "OtherName");
    }

    [Fact]
    public void Field_scope_selects_only_that_field()
    {
        var product = Entity("Product");
        var set = new ModelChangeSet
        {
            Changes =
            [
                new UpdateFieldType(Field("Product", "ProductName"), product),
                new UpdateFieldType(Field("Product", "ProductSku"), product),
            ],
        };

        var result = ModelChangeFilter.ForConcept(set, new PromoteScope(PromoteConcept.Field, "ProductName"));

        result.Changes.ShouldHaveSingleItem();
        result.Of<UpdateFieldType>().Single().Field.Id.ShouldBe("ProductName");
    }

    [Fact]
    public void Cvl_scope_bundles_its_values()
    {
        var set = new ModelChangeSet
        {
            Changes =
            [
                new AddCvl(Cvl("Color")),
                new AddCvlValue("Color", new CvlValue("red", new LocaleString("Red"))),
                new UpdateCvlValue("Color", 7, new CvlValue("blue", new LocaleString("Blue"))),
                new AddCvlValue("Size", new CvlValue("L", new LocaleString("Large"))),
            ],
        };

        var result = ModelChangeFilter.ForConcept(set, new PromoteScope(PromoteConcept.Cvl, "Color"));

        result.Changes.Count.ShouldBe(3); // definition + two Color values
        result.Of<AddCvlValue>().ShouldAllBe(a => a.CvlId == "Color");
    }

    [Fact]
    public void CvlValue_scope_selects_single_value()
    {
        var set = new ModelChangeSet
        {
            Changes =
            [
                new AddCvlValue("Color", new CvlValue("red", new LocaleString("Red"))),
                new AddCvlValue("Color", new CvlValue("blue", new LocaleString("Blue"))),
            ],
        };

        var result = ModelChangeFilter.ForConcept(set, new PromoteScope(PromoteConcept.CvlValue, "Color", CvlKey: "red"));

        result.Changes.ShouldHaveSingleItem();
        result.Of<AddCvlValue>().Single().Value.Key.ShouldBe("red");
    }

    [Fact]
    public void ForConcepts_unions_and_dedups()
    {
        var product = Entity("Product");
        var nameField = new AddFieldType(Field("Product", "ProductName"), product);
        var set = new ModelChangeSet
        {
            Changes = [new AddEntityType(product), nameField],
        };

        // The field matches both the EntityType scope (by owner) and the Field scope (by id) —
        // it must appear once.
        var result = ModelChangeFilter.ForConcepts(set,
        [
            new PromoteScope(PromoteConcept.EntityType, "Product"),
            new PromoteScope(PromoteConcept.Field, "ProductName"),
        ]);

        result.Changes.Count.ShouldBe(2);
        result.Changes.Count(c => c is AddFieldType).ShouldBe(1);
    }

    [Fact]
    public void Empty_scope_list_yields_no_changes()
    {
        var set = new ModelChangeSet { Changes = [new AddCvl(Cvl("Color"))] };
        ModelChangeFilter.ForConcepts(set, System.Array.Empty<PromoteScope>()).Changes.ShouldBeEmpty();
    }
}
