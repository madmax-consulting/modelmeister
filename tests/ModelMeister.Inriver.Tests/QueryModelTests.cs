using System.Collections.Generic;
using System.Linq;
using inRiver.Remoting.Query;
using ModelMeister.Inriver.WorkAreas;
using ModelMeister.Inriver.WorkAreas.Query;
using Shouldly;
using Xunit;
using IrQuery = inRiver.Remoting.Query.Query;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Pins the typed query model that backs the GUI builder, field-level diff and cross-env validity: the
/// <see cref="ComplexQuery"/> ↔ <see cref="QueryModel"/> mapping round-trips faithfully (for the modelled
/// parts), preserves completeness/specification on edit, and the diff + validator produce the expected lines.
/// </summary>
public class QueryModelTests
{
    private static ComplexQuery SampleQuery() => new()
    {
        EntityTypeId = "Product",
        ChannelId = 42,
        DataQuery = new IrQuery
        {
            Join = Join.And,
            Criteria = new List<Criteria>
            {
                new() { FieldTypeId = "ProductName", Operator = Operator.Contains, Value = "widget" },
                new() { FieldTypeId = "ProductPrice", Operator = Operator.GreaterThan, Value = "10" },
            },
        },
        SystemQuery = new SystemQuery
        {
            CreatedBy = "alice",
            CreatedByOperator = Operator.Equal,
            Completeness = 80,
            CompletenessOperator = Operator.GreaterThanOrEqual,
        },
        LinkQuery = new LinkQuery
        {
            LinkTypeId = "ProductItem",
            Direction = LinkDirection.OutBound,
            SourceEntityTypeId = "Product",
            TargetEntityTypeId = "Item",
        },
    };

    [Fact]
    public void ToModel_extracts_data_system_and_link()
    {
        var model = QueryMapper.ToModel(SampleQuery());

        model.EntityTypeId.ShouldBe("Product");
        model.ChannelId.ShouldBe(42);
        model.DataQuery!.Join.ShouldBe(QJoin.And);
        model.DataQuery.Criteria.Select(c => c.FieldTypeId).ShouldBe(new[] { "ProductName", "ProductPrice" });
        model.SystemCriteria.Select(c => c.Field).ShouldContain(SystemField.CreatedBy);
        model.SystemCriteria.Select(c => c.Field).ShouldContain(SystemField.Completeness);
        model.LinkQuery!.LinkTypeId.ShouldBe("ProductItem");
        model.LinkQuery.Direction.ShouldBe(QLinkDirection.OutBound);
    }

    [Fact]
    public void Round_trips_through_model_preserving_serialized_form()
    {
        var original = SampleQuery();
        var back = QueryMapper.ToComplexQuery(QueryMapper.ToModel(original), original);

        WorkAreaService.SerializeQuery(back).ShouldBe(WorkAreaService.SerializeQuery(original));
    }

    [Fact]
    public void Json_bridge_round_trips()
    {
        var json = WorkAreaService.SerializeQuery(SampleQuery());

        var model = QueryMapper.ToModel(WorkAreaService.DeserializeQuery(json));
        var rebuiltJson = WorkAreaService.SerializeQuery(
            QueryMapper.ToComplexQuery(model, WorkAreaService.DeserializeQuery(json)));

        rebuiltJson.ShouldBe(json);
    }

    [Fact]
    public void Nested_subquery_round_trips_byte_stable()
    {
        // Closes the previously-uncovered regression: a nested DataQuery.SubQuery must survive
        // ComplexQuery -> model -> ComplexQuery and JSON -> model -> JSON without being flattened.
        var original = new ComplexQuery
        {
            EntityTypeId = "Product",
            DataQuery = new IrQuery
            {
                Join = Join.And,
                Criteria = [new Criteria { FieldTypeId = "ProductName", Operator = Operator.Contains, Value = "widget" }],
                SubQuery = new IrQuery
                {
                    Join = Join.Or,
                    Criteria =
                    [
                        new Criteria { FieldTypeId = "Status", Operator = Operator.Equal, Value = "Active" },
                        new Criteria { FieldTypeId = "Status", Operator = Operator.Equal, Value = "Pending" },
                    ],
                },
            },
        };

        // ComplexQuery -> model -> ComplexQuery preserves the nested group.
        var model = QueryMapper.ToModel(original);
        model.DataQuery!.SubQuery.ShouldNotBeNull();
        model.DataQuery.SubQuery!.Join.ShouldBe(QJoin.Or);
        model.DataQuery.SubQuery.Criteria.Select(c => c.Value).ShouldBe(new[] { "Active", "Pending" });

        var back = QueryMapper.ToComplexQuery(model, original);
        WorkAreaService.SerializeQuery(back).ShouldBe(WorkAreaService.SerializeQuery(original));

        // JSON -> model -> JSON is byte-stable.
        var json = WorkAreaService.SerializeQuery(original);
        var rebuiltJson = WorkAreaService.SerializeQuery(
            QueryMapper.ToComplexQuery(QueryMapper.ToModel(WorkAreaService.DeserializeQuery(json)), WorkAreaService.DeserializeQuery(json)));
        rebuiltJson.ShouldBe(json);
    }

    [Fact]
    public void Validator_warns_on_value_required_operator_with_no_value()
    {
        var model = new QueryModel
        {
            DataQuery = new CriteriaGroup
            {
                Join = QJoin.And,
                Criteria = [new CriterionModel { FieldTypeId = "ProductName", Operator = QOperator.Equal, Value = null }],
            },
        };

        // Structural checks run even without a catalog.
        var warnings = QueryValidator.Validate(model, QueryMetadata.Empty);
        warnings.ShouldContain(w => w.Contains("ProductName") && w.Contains("Equal"));
    }

    [Fact]
    public void Validator_warns_on_bool_operator_against_non_bool_field()
    {
        var meta = new QueryMetadata(
            EntityTypeIds: ["Product"],
            FieldTypeIdsByEntityType: new Dictionary<string, IReadOnlyList<string>> { ["Product"] = ["ProductName"] },
            AllFieldTypeIds: ["ProductName"],
            LinkTypeIds: [],
            FieldDataTypeById: new Dictionary<string, string> { ["ProductName"] = "String" });

        var model = new QueryModel
        {
            DataQuery = new CriteriaGroup
            {
                Join = QJoin.And,
                Criteria = [new CriterionModel { FieldTypeId = "ProductName", Operator = QOperator.IsTrue }],
            },
        };

        var warnings = QueryValidator.Validate(model, meta);
        warnings.ShouldContain(w => w.Contains("ProductName") && w.Contains("IsTrue"));
    }

    [Fact]
    public void Summary_describes_a_nested_query()
    {
        var model = QueryMapper.ToModel(new ComplexQuery
        {
            EntityTypeId = "Product",
            DataQuery = new IrQuery
            {
                Join = Join.And,
                Criteria = [new Criteria { FieldTypeId = "ProductName", Operator = Operator.Contains, Value = "widget" }],
            },
        });

        var summary = QuerySummary.Describe(model);
        summary.ShouldContain("Product");
        summary.ShouldContain("ProductName");
        summary.ShouldContain("widget");
    }

    [Fact]
    public void Preserves_completeness_and_specification_on_edit()
    {
        var q = SampleQuery();
        q.CompletenessQuery = new CompletenessQuery { CompletenessDefinitionId = 5 };
        q.SpecificationQuery = new SpecificationQuery { EntityId = "7" };

        var model = QueryMapper.ToModel(q);
        model.HasUnsupportedParts.ShouldBeTrue();

        var back = QueryMapper.ToComplexQuery(model, q);
        back.CompletenessQuery.ShouldNotBeNull();
        back.SpecificationQuery.ShouldNotBeNull();
    }

    [Fact]
    public void Diff_describes_added_and_removed_data_criteria()
    {
        var left = WorkAreaService.SerializeQuery(new ComplexQuery
        {
            EntityTypeId = "Product",
            DataQuery = new IrQuery { Join = Join.And, Criteria = [new Criteria { FieldTypeId = "Status", Operator = Operator.Equal, Value = "Active" }] },
        });
        var right = WorkAreaService.SerializeQuery(new ComplexQuery
        {
            EntityTypeId = "Product",
            DataQuery = new IrQuery { Join = Join.And, Criteria = [new Criteria { FieldTypeId = "Status", Operator = Operator.Equal, Value = "Archived" }] },
        });

        var lines = QueryDiff.Describe(left, right);

        lines.ShouldContain(l => l.StartsWith("+") && l.Contains("Archived"));
        lines.ShouldContain(l => l.StartsWith("−") && l.Contains("Active"));
    }

    [Fact]
    public void Diff_of_equal_queries_is_empty()
    {
        var json = WorkAreaService.SerializeQuery(SampleQuery());
        QueryDiff.Describe(json, json).ShouldBeEmpty();
    }

    [Fact]
    public void Validator_warns_on_unknown_ids_and_is_silent_without_metadata()
    {
        var model = new QueryModel
        {
            EntityTypeId = "Ghost",
            DataQuery = new CriteriaGroup { Join = QJoin.And, Criteria = [new CriterionModel { FieldTypeId = "NoSuchField", Operator = QOperator.Equal, Value = "x" }] },
        };

        var meta = new QueryMetadata(
            EntityTypeIds: ["Product"],
            FieldTypeIdsByEntityType: new Dictionary<string, IReadOnlyList<string>> { ["Product"] = ["ProductName"] },
            AllFieldTypeIds: ["ProductName"],
            LinkTypeIds: ["ProductItem"]);

        var warnings = QueryValidator.Validate(model, meta);
        warnings.ShouldContain(w => w.Contains("Ghost"));
        warnings.ShouldContain(w => w.Contains("NoSuchField"));

        QueryValidator.Validate(model, QueryMetadata.Empty).ShouldBeEmpty();
    }
}
