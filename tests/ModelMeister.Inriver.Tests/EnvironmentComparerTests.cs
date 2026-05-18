using Shouldly;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Inriver.Tests;

public class EnvironmentComparerTests
{
    [Fact]
    public void Identical_models_have_zero_differences()
    {
        var a = Live(entityIds: new[] { "Product", "Supplier" }, cvls: new[] { "Markets" });
        var b = Live(entityIds: new[] { "Product", "Supplier" }, cvls: new[] { "Markets" });
        EnvironmentComparer.Compare(a, b).TotalDifferences.ShouldBe(0);
    }

    [Fact]
    public void Reports_only_in_left_and_only_in_right_separately()
    {
        var a = Live(entityIds: new[] { "Product", "Supplier" }, cvls: new[] { "Markets" });
        var b = Live(entityIds: new[] { "Product", "Channel" }, cvls: new[] { "Markets", "Brands" });
        var diff = EnvironmentComparer.Compare(a, b);

        diff.EntityTypes.OnlyInLeft.ShouldContain("Supplier");
        diff.EntityTypes.OnlyInRight.ShouldContain("Channel");
        diff.Cvls.OnlyInRight.ShouldContain("Brands");
    }

    [Fact]
    public void Detects_changed_field_properties()
    {
        LiveFieldType MakeField(bool mandatory) => new()
        {
            Id = "ProductName", EntityTypeId = "Product",
            Name = new LocaleString("Name"),
            DataType = Datatype.LocaleString,
            Mandatory = mandatory, IsDisplayName = true,
        };
        var a = LiveWithFields("Product", new[] { MakeField(true) });
        var b = LiveWithFields("Product", new[] { MakeField(false) });
        var diff = EnvironmentComparer.Compare(a, b);
        diff.ChangedFields.Single().Differences.Any(d => d.Property == "Mandatory").ShouldBeTrue();
    }

    [Fact]
    public void Detects_only_in_left_and_right_cvl_values()
    {
        var leftCvl = new LiveCvl
        {
            Id = "Markets", DataTypeRaw = "String", DataType = CvlDataType.String, CustomValueList = true,
            Values = new List<LiveCvlValue>
            {
                new() { Id = 1, CvlId = "Markets", Key = "EU", Value = new LocaleString("Europe") },
                new() { Id = 2, CvlId = "Markets", Key = "US", Value = new LocaleString("US") },
            },
        };
        var rightCvl = new LiveCvl
        {
            Id = "Markets", DataTypeRaw = "String", DataType = CvlDataType.String, CustomValueList = true,
            Values = new List<LiveCvlValue>
            {
                new() { Id = 1, CvlId = "Markets", Key = "EU", Value = new LocaleString("Europe") },
                new() { Id = 3, CvlId = "Markets", Key = "APAC", Value = new LocaleString("Asia-Pacific") },
            },
        };
        var a = new LiveModel
        {
            EnvironmentUrl = "a", CapturedUtc = DateTime.UtcNow,
            EntityTypes = Array.Empty<LiveEntityType>(),
            Cvls = new[] { leftCvl }, Categories = Array.Empty<LiveCategory>(),
            Fieldsets = Array.Empty<LiveFieldset>(), LinkTypes = Array.Empty<LiveLinkType>(),
            Roles = Array.Empty<LiveRole>(), Permissions = Array.Empty<LivePermission>(),
            CompletenessDefinitions = Array.Empty<LiveCompletenessDefinition>(),
            RestrictedFieldPermissions = Array.Empty<LiveRestrictedFieldPermission>(),
            Languages = new[] { "en" },
        };
        var b = new LiveModel
        {
            EnvironmentUrl = "b", CapturedUtc = DateTime.UtcNow,
            EntityTypes = Array.Empty<LiveEntityType>(),
            Cvls = new[] { rightCvl }, Categories = Array.Empty<LiveCategory>(),
            Fieldsets = Array.Empty<LiveFieldset>(), LinkTypes = Array.Empty<LiveLinkType>(),
            Roles = Array.Empty<LiveRole>(), Permissions = Array.Empty<LivePermission>(),
            CompletenessDefinitions = Array.Empty<LiveCompletenessDefinition>(),
            RestrictedFieldPermissions = Array.Empty<LiveRestrictedFieldPermission>(),
            Languages = new[] { "en" },
        };
        var diff = EnvironmentComparer.Compare(a, b);
        var delta = diff.CvlValueChanges.Single(c => c.CvlId == "Markets");
        delta.OnlyInLeft.ShouldContain("US");
        delta.OnlyInRight.ShouldContain("APAC");
    }

    static LiveModel Live(string[] entityIds, string[] cvls)
        => new()
        {
            EnvironmentUrl = "test", CapturedUtc = DateTime.UtcNow,
            EntityTypes = entityIds.Select(id => new LiveEntityType { Id = id, Name = new LocaleString(id), Fields = Array.Empty<LiveFieldType>() }).ToList(),
            Cvls = cvls.Select(id => new LiveCvl { Id = id, DataTypeRaw = "String", DataType = CvlDataType.String, Values = Array.Empty<LiveCvlValue>() }).ToList(),
            Categories = Array.Empty<LiveCategory>(),
            Fieldsets = Array.Empty<LiveFieldset>(),
            LinkTypes = Array.Empty<LiveLinkType>(),
            Roles = Array.Empty<LiveRole>(),
            Permissions = Array.Empty<LivePermission>(),
            CompletenessDefinitions = Array.Empty<LiveCompletenessDefinition>(),
            RestrictedFieldPermissions = Array.Empty<LiveRestrictedFieldPermission>(),
            Languages = new[] { "en" },
        };

    static LiveModel LiveWithFields(string entityId, IEnumerable<LiveFieldType> fields)
    {
        var fs = fields.ToList();
        return new LiveModel
        {
            EnvironmentUrl = "test", CapturedUtc = DateTime.UtcNow,
            EntityTypes = new[]
            {
                new LiveEntityType { Id = entityId, Name = new LocaleString(entityId), Fields = fs },
            },
            Cvls = Array.Empty<LiveCvl>(),
            Categories = Array.Empty<LiveCategory>(),
            Fieldsets = Array.Empty<LiveFieldset>(),
            LinkTypes = Array.Empty<LiveLinkType>(),
            Roles = Array.Empty<LiveRole>(),
            Permissions = Array.Empty<LivePermission>(),
            CompletenessDefinitions = Array.Empty<LiveCompletenessDefinition>(),
            RestrictedFieldPermissions = Array.Empty<LiveRestrictedFieldPermission>(),
            Languages = new[] { "en" },
        };
    }
}
