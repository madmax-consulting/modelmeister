using Shouldly;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Model;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Primitives;
using Xunit;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Idempotency: after the code-defined model has been applied, diffing the same model against a snapshot
/// that mirrors what the applier writes must produce zero changes — otherwise apply-then-diff loops forever.
/// These pin the bug fixes in <see cref="ModelDiffer"/> FieldDiffers (DefaultValue, CvlId, Description, nullable
/// TrackChanges/ExcludeFromDefaultView).
/// </summary>
public class IdempotencyDiffTests
{
    [Fact]
    public void Field_with_null_TrackChanges_and_ExcludeFromDefaultView_is_idempotent_against_live_readthrough()
    {
        // Code model: a field that doesn't pin TrackChanges or ExcludeFromDefaultView.
        var lf = MakeField<int>("ProductCount", Datatype.Integer, trackChanges: null, exclude: null, defaultValue: null);
        var owner = MakeEntity("Product", lf);
        var code = new LoadedModel { EntityTypes = new[] { owner } };

        // Live: server side has TrackChanges=true (derived elsewhere) and ExcludeFromDefaultView=false.
        var liveFt = new LiveFieldType
        {
            Id = "ProductCount",
            EntityTypeId = "Product",
            Name = new LocaleString("Count"),
            DataType = Datatype.Integer,
            TrackChanges = true,
            ExcludeFromDefaultView = false,
        };
        var live = MakeLive("Product", liveFt);

        var diff = ModelDiffer.Diff(code, live);
        diff.Of<UpdateFieldType>().ShouldBeEmpty();
    }

    [Fact]
    public void Field_with_changed_DefaultValue_produces_UpdateFieldType()
    {
        var lf = MakeField<string>("ProductCode", Datatype.String, defaultValue: "ABC");
        var owner = MakeEntity("Product", lf);
        var code = new LoadedModel { EntityTypes = new[] { owner } };

        var liveFt = new LiveFieldType
        {
            Id = "ProductCode",
            EntityTypeId = "Product",
            Name = new LocaleString("Code"),
            DataType = Datatype.String,
            DefaultValue = "XYZ",
        };
        var live = MakeLive("Product", liveFt);

        var diff = ModelDiffer.Diff(code, live);
        diff.Of<UpdateFieldType>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Field_with_matching_DefaultValue_is_idempotent()
    {
        var lf = MakeField<string>("ProductCode", Datatype.String, defaultValue: "ABC");
        var owner = MakeEntity("Product", lf);
        var code = new LoadedModel { EntityTypes = new[] { owner } };

        var liveFt = new LiveFieldType
        {
            Id = "ProductCode",
            EntityTypeId = "Product",
            Name = new LocaleString("Code"),
            DataType = Datatype.String,
            DefaultValue = "ABC",
        };
        var live = MakeLive("Product", liveFt);

        var diff = ModelDiffer.Diff(code, live);
        diff.Of<UpdateFieldType>().ShouldBeEmpty();
    }

    [Fact]
    public void Field_CvlId_change_produces_UpdateFieldType()
    {
        var loadedCvlNew = new LoadedCvl
        {
            ClrType = typeof(NewCvl),
            CvlId = "NewCvl",
            DataType = CvlDataType.String,
        };
        var lf = MakeField<CvlKey>("ProductBrand", Datatype.Cvl, cvl: typeof(NewCvl));
        var owner = MakeEntity("Product", lf);
        var code = new LoadedModel { EntityTypes = new[] { owner }, Cvls = new[] { loadedCvlNew } };

        var liveFt = new LiveFieldType
        {
            Id = "ProductBrand",
            EntityTypeId = "Product",
            Name = new LocaleString("Brand"),
            DataType = Datatype.Cvl,
            CvlId = "OldCvl",
        };
        var live = MakeLive("Product", liveFt);

        var diff = ModelDiffer.Diff(code, live);
        diff.Of<UpdateFieldType>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Field_CvlId_matching_is_idempotent()
    {
        var loadedCvl = new LoadedCvl
        {
            ClrType = typeof(NewCvl),
            CvlId = "NewCvl",
            DataType = CvlDataType.String,
        };
        var lf = MakeField<CvlKey>("ProductBrand", Datatype.Cvl, cvl: typeof(NewCvl));
        var owner = MakeEntity("Product", lf);
        var code = new LoadedModel { EntityTypes = new[] { owner }, Cvls = new[] { loadedCvl } };

        var liveFt = new LiveFieldType
        {
            Id = "ProductBrand",
            EntityTypeId = "Product",
            Name = new LocaleString("Brand"),
            DataType = Datatype.Cvl,
            CvlId = "NewCvl",
        };
        var live = MakeLive("Product", liveFt);

        var diff = ModelDiffer.Diff(code, live);
        diff.Of<UpdateFieldType>().ShouldBeEmpty();
    }

    [Fact]
    public void Field_Description_change_only_surfaces_when_overwrite_policy_set()
    {
        var lf = MakeField<int>("ProductCount", Datatype.Integer, description: new LocaleString("Count of widgets"));
        var owner = MakeEntity("Product", lf);
        var code = new LoadedModel { EntityTypes = new[] { owner } };

        var liveFt = new LiveFieldType
        {
            Id = "ProductCount",
            EntityTypeId = "Product",
            Name = new LocaleString("Count"),
            Description = new LocaleString("Old description"),
            DataType = Datatype.Integer,
        };
        var live = MakeLive("Product", liveFt);

        // Default policy ignores description differences.
        ModelDiffer.Diff(code, live).Of<UpdateFieldType>().ShouldBeEmpty();

        var policy = MergePolicy.Default with { OverwriteNamesAndDescriptions = true };
        ModelDiffer.Diff(code, live, policy).Of<UpdateFieldType>().ShouldHaveSingleItem();
    }

    // ---------- helpers ----------
    private static LoadedField MakeField<T>(
        string id,
        Datatype dt,
        bool? trackChanges = null,
        bool? exclude = null,
        object? defaultValue = null,
        LocaleString? description = null,
        Type? cvl = null)
    {
        var field = new Field<T>
        {
            TrackChanges = trackChanges,
            ExcludeFromDefaultView = exclude,
            DefaultValue = defaultValue,
            Description = description,
            Cvl = cvl,
        };
        return new LoadedField
        {
            Field = field,
            Id = id,
            EntityTypeId = "Product",
            PropertyName = id,
            Name = new LocaleString("Count"),
            DataType = dt,
        };
    }

    private static LoadedEntityType MakeEntity(string id, params LoadedField[] fields) => new()
    {
        ClrType = typeof(object),
        EntityTypeId = id,
        Name = new LocaleString(id),
        Fields = fields.ToList(),
    };

    private static LiveModel MakeLive(string entityId, params LiveFieldType[] fields) => new()
    {
        EnvironmentUrl = "test",
        CapturedUtc = DateTime.UtcNow,
        EntityTypes = new[]
        {
            new LiveEntityType
            {
                Id = entityId,
                Name = new LocaleString(entityId),
                Fields = fields,
            },
        },
    };

    /// <summary>Placeholder CVL CLR types — only their Type identity matters for the diff.</summary>
    private abstract class NewCvl : ModelMeister.Model.Cvl { }
}
