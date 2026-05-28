using System.Collections.Generic;
using System.Linq;
using ModelMeister.Inriver.WorkAreas;
using ModelMeister.Inriver.WorkAreas.Query;
using ModelMeister.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace ModelMeister.Ui.Tests;

/// <summary>
/// The query-builder view-model is dependency-free, so it can be exercised without the GUI: loading an
/// existing query populates the criterion rows, edits flow back into the model, the result JSON round-trips,
/// and validity warnings surface for ids the (provided) env metadata doesn't know.
/// </summary>
public class QueryEditorViewModelTests
{
    private static QueryMetadata Meta() => new(
        EntityTypeIds: ["Product"],
        FieldTypeIdsByEntityType: new Dictionary<string, IReadOnlyList<string>> { ["Product"] = ["ProductName"] },
        AllFieldTypeIds: ["ProductName"],
        LinkTypeIds: ["ProductItem"]);

    [Fact]
    public void Loads_existing_query_into_rows()
    {
        var json = WorkAreaService.SerializeQuery(new inRiver.Remoting.Query.ComplexQuery
        {
            EntityTypeId = "Product",
            DataQuery = new inRiver.Remoting.Query.Query
            {
                Join = inRiver.Remoting.Query.Join.And,
                Criteria = [new inRiver.Remoting.Query.Criteria { FieldTypeId = "ProductName", Operator = inRiver.Remoting.Query.Operator.Contains, Value = "widget" }],
            },
        });

        var vm = new QueryEditorViewModel("Pending review", json, Meta());

        vm.EntityTypeId.ShouldBe("Product");
        vm.DataCriteria.Count.ShouldBe(1);
        vm.DataCriteria[0].FieldTypeId.ShouldBe("ProductName");
        vm.DataCriteria[0].Operator.ShouldBe(QOperator.Contains);
        vm.DataCriteria[0].Value.ShouldBe("widget");
        vm.HasWarnings.ShouldBeFalse();
    }

    [Fact]
    public void Added_criterion_flows_into_built_model_and_result_json()
    {
        var vm = new QueryEditorViewModel("New", existingQueryJson: null, Meta()) { EntityTypeId = "Product" };
        vm.AddDataCriterionCommand.Execute(null);
        vm.DataCriteria[0].FieldTypeId = "ProductName";
        vm.DataCriteria[0].Operator = QOperator.Equal;
        vm.DataCriteria[0].Value = "abc";

        var model = vm.BuildModel();
        model.EntityTypeId.ShouldBe("Product");
        model.DataQuery!.Criteria.Single().FieldTypeId.ShouldBe("ProductName");

        vm.ConfirmCommand.Execute(null);
        vm.Result.ShouldBe(true);
        vm.ResultJson.ShouldNotBeNull();
        // Result JSON re-parses back to the same field criterion.
        var back = QueryMapper.ToModel(WorkAreaService.DeserializeQuery(vm.ResultJson));
        back.DataQuery!.Criteria.Single().FieldTypeId.ShouldBe("ProductName");
    }

    [Fact]
    public void Unknown_field_raises_a_validity_warning()
    {
        var vm = new QueryEditorViewModel("New", existingQueryJson: null, Meta()) { EntityTypeId = "Ghost" };
        vm.AddDataCriterionCommand.Execute(null);
        vm.DataCriteria[0].FieldTypeId = "NoSuchField";

        vm.HasWarnings.ShouldBeTrue();
        vm.Validation.ShouldContain("Ghost");
        vm.Validation.ShouldContain("NoSuchField");
    }
}
