using Shouldly;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Merge-policy safety: with default policy, the differ never emits Delete records even when
/// the live side has concepts the code model does not. With <c>AllowDeletes</c>, deletions surface.
/// </summary>
public class MergePolicyDiffTests
{
    [Fact]
    public void EntityType_missing_from_code_is_not_deleted_by_default()
    {
        var code = new LoadedModel { EntityTypes = Array.Empty<LoadedEntityType>() };
        var live = new LiveModel
        {
            EnvironmentUrl = "test",
            CapturedUtc = DateTime.UtcNow,
            EntityTypes = new[]
            {
                new LiveEntityType { Id = "Orphan", Name = new LocaleString("Orphan") },
            },
        };
        var diff = ModelDiffer.Diff(code, live);
        diff.Of<DeleteEntityType>().ShouldBeEmpty();
    }

    [Fact]
    public void EntityType_missing_from_code_is_deleted_when_allowed()
    {
        var code = new LoadedModel { EntityTypes = Array.Empty<LoadedEntityType>() };
        var live = new LiveModel
        {
            EnvironmentUrl = "test",
            CapturedUtc = DateTime.UtcNow,
            EntityTypes = new[]
            {
                new LiveEntityType { Id = "Orphan", Name = new LocaleString("Orphan") },
            },
        };
        var policy = MergePolicy.Default with { AllowDeletes = true };
        var diff = ModelDiffer.Diff(code, live, policy);
        var dels = diff.Of<DeleteEntityType>().ToList();
        dels.Count.ShouldBe(1);
        dels[0].Id.ShouldBe("Orphan");
    }

    [Fact]
    public void Datatype_change_is_warned_by_default_not_applied()
    {
        var owner = new LoadedEntityType
        {
            ClrType = typeof(object),
            EntityTypeId = "Product",
            Name = new LocaleString("Product"),
            Fields = new List<LoadedField>
            {
                new()
                {
                    Field = new Model.Field<int>(),
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
                        new LiveFieldType { Id = "ProductCount", EntityTypeId = "Product", Name = new LocaleString("Count"), DataType = Datatype.String },
                    },
                },
            },
        };
        var diff = ModelDiffer.Diff(code, live);
        diff.Of<ChangeFieldDatatype>().ShouldBeEmpty();
        diff.Warnings.ShouldContain(w => w.Code == "DatatypeChangeBlocked");
    }
}
