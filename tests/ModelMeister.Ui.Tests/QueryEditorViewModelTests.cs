using System.Collections.Generic;
using System.Linq;
using ModelMeister.Inriver.WorkAreas;
using ModelMeister.Inriver.WorkAreas.Query;
using ModelMeister.Ui.ViewModels;
using Shouldly;
using Xunit;
using IrComplex = inRiver.Remoting.Query.ComplexQuery;
using IrQuery = inRiver.Remoting.Query.Query;

namespace ModelMeister.Ui.Tests;

/// <summary>
/// The query-builder view-model is dependency-free, so it can be exercised without the GUI: loading an
/// existing query populates the recursive group tree, edits flow back into the model, the result JSON
/// round-trips, nested sub-queries survive an edit-save (the F1 data-loss regression), the raw-JSON toggle
/// round-trips, and validity warnings surface for ids the (provided) env metadata doesn't know.
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
        var json = WorkAreaService.SerializeQuery(new IrComplex
        {
            EntityTypeId = "Product",
            DataQuery = new IrQuery
            {
                Join = inRiver.Remoting.Query.Join.And,
                Criteria = [new inRiver.Remoting.Query.Criteria { FieldTypeId = "ProductName", Operator = inRiver.Remoting.Query.Operator.Contains, Value = "widget" }],
            },
        });

        var vm = new QueryEditorViewModel("Pending review", json, Meta());

        vm.EntityTypeId.ShouldBe("Product");
        vm.DataRoot.Criteria.Count.ShouldBe(1);
        vm.DataRoot.Criteria[0].FieldTypeId.ShouldBe("ProductName");
        vm.DataRoot.Criteria[0].Operator.ShouldBe(QOperator.Contains);
        vm.DataRoot.Criteria[0].Value.ShouldBe("widget");
        vm.HasWarnings.ShouldBeFalse();
    }

    [Fact]
    public void Added_criterion_flows_into_built_model_and_result_json()
    {
        var vm = new QueryEditorViewModel("New", existingQueryJson: null, Meta()) { EntityTypeId = "Product" };
        vm.DataRoot.AddCriterionCommand.Execute(null);
        vm.DataRoot.Criteria[0].FieldTypeId = "ProductName";
        vm.DataRoot.Criteria[0].Operator = QOperator.Equal;
        vm.DataRoot.Criteria[0].Value = "abc";

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
        vm.DataRoot.AddCriterionCommand.Execute(null);
        vm.DataRoot.Criteria[0].FieldTypeId = "NoSuchField";

        vm.HasWarnings.ShouldBeTrue();
        vm.Validation.ShouldContain("Ghost");
        vm.Validation.ShouldContain("NoSuchField");
    }

    [Fact]
    public void Edit_save_preserves_existing_nested_group()
    {
        // A query with a nested SubQuery (data: outer AND with one criterion, inner OR with two).
        var json = WorkAreaService.SerializeQuery(new IrComplex
        {
            EntityTypeId = "Product",
            DataQuery = new IrQuery
            {
                Join = inRiver.Remoting.Query.Join.And,
                Criteria = [new inRiver.Remoting.Query.Criteria { FieldTypeId = "ProductName", Operator = inRiver.Remoting.Query.Operator.Contains, Value = "widget" }],
                SubQuery = new IrQuery
                {
                    Join = inRiver.Remoting.Query.Join.Or,
                    Criteria =
                    [
                        new inRiver.Remoting.Query.Criteria { FieldTypeId = "Status", Operator = inRiver.Remoting.Query.Operator.Equal, Value = "Active" },
                        new inRiver.Remoting.Query.Criteria { FieldTypeId = "Status", Operator = inRiver.Remoting.Query.Operator.Equal, Value = "Pending" },
                    ],
                },
            },
        });

        var vm = new QueryEditorViewModel("Has nested group", json, Meta());

        // The nested group is surfaced in the UI tree.
        vm.DataRoot.SubGroups.Count.ShouldBe(1);
        vm.DataRoot.SubGroups[0].Join.ShouldBe(QJoin.Or);
        vm.DataRoot.SubGroups[0].Criteria.Count.ShouldBe(2);

        // Saving without touching the nested group must not flatten it — byte-stable round-trip.
        vm.ConfirmCommand.Execute(null);
        vm.ResultJson.ShouldBe(json);

        var back = QueryMapper.ToModel(WorkAreaService.DeserializeQuery(vm.ResultJson));
        back.DataQuery!.SubQuery.ShouldNotBeNull();
        back.DataQuery.SubQuery!.Join.ShouldBe(QJoin.Or);
        back.DataQuery.SubQuery.Criteria.Select(c => c.Value).ShouldBe(new[] { "Active", "Pending" });
    }

    [Fact]
    public void Nested_group_builds_expected_complexquery()
    {
        var vm = new QueryEditorViewModel("Build nested", existingQueryJson: null, Meta()) { EntityTypeId = "Product" };
        vm.DataRoot.AddCriterionCommand.Execute(null);
        vm.DataRoot.Criteria[0].FieldTypeId = "ProductName";
        vm.DataRoot.Criteria[0].Operator = QOperator.Contains;
        vm.DataRoot.Criteria[0].Value = "widget";

        vm.DataRoot.AddGroupCommand.Execute(null);
        var sub = vm.DataRoot.SubGroups[0];
        sub.Join = QJoin.Or;
        sub.AddCriterionCommand.Execute(null);
        sub.Criteria[0].FieldTypeId = "ProductName";
        sub.Criteria[0].Operator = QOperator.Equal;
        sub.Criteria[0].Value = "x";

        var model = vm.BuildModel();
        model.DataQuery!.Criteria.Single().FieldTypeId.ShouldBe("ProductName");
        model.DataQuery.SubQuery.ShouldNotBeNull();
        model.DataQuery.SubQuery!.Join.ShouldBe(QJoin.Or);
        model.DataQuery.SubQuery.Criteria.Single().Value.ShouldBe("x");
    }

    [Fact]
    public void Sibling_subgroups_raise_a_reload_fidelity_warning()
    {
        // The UI tree is n-ary, but inriver's wire format is a single SubQuery chain and the load path reads
        // exactly one child per level. Two sibling sub-groups (or a group with both criteria and a sub-group)
        // left-nest on save and would reload reshaped — DESIGN F2 requires this be surfaced as a validity
        // warning rather than silently changing the boolean structure.
        var vm = new QueryEditorViewModel("Two siblings", existingQueryJson: null, Meta()) { EntityTypeId = "Product" };

        // The root group carries its own criterion AND a nested sub-group — the unfoldable shape.
        vm.DataRoot.AddCriterionCommand.Execute(null);
        vm.DataRoot.Criteria[0].FieldTypeId = "ProductName";
        vm.DataRoot.Criteria[0].Operator = QOperator.Contains;
        vm.DataRoot.Criteria[0].Value = "widget";

        vm.DataRoot.AddGroupCommand.Execute(null);
        var sub = vm.DataRoot.SubGroups[0];
        sub.AddCriterionCommand.Execute(null);
        sub.Criteria[0].FieldTypeId = "ProductName";
        sub.Criteria[0].Operator = QOperator.Equal;
        sub.Criteria[0].Value = "x";

        vm.HasWarnings.ShouldBeTrue();
        vm.Validation.ShouldContain("sub-group");
    }

    [Fact]
    public void Raw_json_toggle_round_trips()
    {
        var json = WorkAreaService.SerializeQuery(new IrComplex
        {
            EntityTypeId = "Product",
            DataQuery = new IrQuery
            {
                Join = inRiver.Remoting.Query.Join.And,
                Criteria = [new inRiver.Remoting.Query.Criteria { FieldTypeId = "ProductName", Operator = inRiver.Remoting.Query.Operator.Equal, Value = "abc" }],
            },
        });

        var vm = new QueryEditorViewModel("Raw round-trip", json, Meta());

        // Enter raw view: the serialized JSON matches what was loaded.
        vm.ShowRawJson = true;
        vm.RawJson.ShouldBe(json);

        // Leave raw view (no edits): rows repopulate and a re-save yields the same JSON.
        vm.ShowRawJson = false;
        vm.DataRoot.Criteria.Single().FieldTypeId.ShouldBe("ProductName");
        vm.ConfirmCommand.Execute(null);
        vm.ResultJson.ShouldBe(json);
    }

    [Fact]
    public void Invalid_raw_json_blocks_leaving_raw_view()
    {
        var vm = new QueryEditorViewModel("Bad JSON", existingQueryJson: null, Meta());
        vm.ShowRawJson = true;
        vm.RawJson = "{ this is not valid json ]";

        vm.ShowRawJson = false; // attempt to leave

        vm.ShowRawJson.ShouldBeTrue();          // stayed in raw view
        vm.RawJsonError.ShouldNotBeNullOrEmpty();
    }
}
