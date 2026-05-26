using Shouldly;
using inRiver.Remoting.Query;
using ModelMeister.Inriver.WorkAreas;
using Xunit;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// The saved query is treated as an opaque JSON blob for display/Excel. These pin that the structural
/// shape (entity type, criteria field/operator) survives a serialize→deserialize→serialize round-trip,
/// so an exported workbook captures the query faithfully enough to inspect and re-apply.
/// </summary>
public class WorkAreaQueryJsonTests
{
    [Fact]
    public void Null_query_serializes_to_null()
    {
        WorkAreaService.SerializeQuery(null).ShouldBeNull();
        WorkAreaService.DeserializeQuery(null).ShouldBeNull();
        WorkAreaService.DeserializeQuery("").ShouldBeNull();
    }

    [Fact]
    public void Query_round_trips_structural_shape()
    {
        var query = new ComplexQuery
        {
            EntityTypeId = "Product",
            DataQuery = new Query
            {
                Join = Join.And,
                Criteria = new List<Criteria>
                {
                    new() { FieldTypeId = "ProductName", Operator = Operator.Contains, Value = "widget" },
                },
            },
        };

        var json = WorkAreaService.SerializeQuery(query);
        json.ShouldNotBeNull();
        json.ShouldContain("Product");

        var back = WorkAreaService.DeserializeQuery(json);
        back.ShouldNotBeNull();
        back!.EntityTypeId.ShouldBe("Product");
        back.DataQuery.ShouldNotBeNull();
        back.DataQuery!.Criteria.ShouldContain(c => c.FieldTypeId == "ProductName" && c.Operator == Operator.Contains);

        // Re-serialization is stable (the blob is a faithful opaque representation).
        WorkAreaService.SerializeQuery(back).ShouldBe(json);
    }
}
