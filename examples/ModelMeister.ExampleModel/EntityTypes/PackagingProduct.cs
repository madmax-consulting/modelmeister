using ModelMeister.ExampleModel.Categories;
using ModelMeister.ExampleModel.CompletenessGroups;
using ModelMeister.ExampleModel.Cvls;
using ModelMeister.ExampleModel.Fieldsets;
using ModelMeister.ExampleModel.LinkTypes;
using ModelMeister.ExampleModel.Roles;
using ModelMeister.Model;
using ModelMeister.Model.Completeness;
using ModelMeister.Model.Expressions;
using ModelMeister.Model.Primitives;
using ModelMeister.Model.Security;

namespace ModelMeister.ExampleModel.EntityTypes;

/// <summary>
/// Flagship entity. Touches the full <see cref="Field"/> option surface (MultiValue, Unique, Mandatory,
/// ReadOnly, Hidden, ExcludeFromDefaultView, TrackChanges, Index, RegExp, NumberOfRows,
/// ShowInEntityOverview, IgnoreFieldInEpiserverExport, Category, Fieldset/Fieldsets, PerMarket,
/// Settings, ReadOnlyFor/HiddenFor/EditableFor/VisibleFor lists, SupportsExpression, DefaultExpression,
/// DefaultValue), every <see cref="CompletenessRuleAttribute"/> kind, and several restriction
/// attributes.
/// </summary>
public sealed class PackagingProduct : TranslatableEntity
{
    public PackagingProduct()
    {
        EntityTypeName = new LocaleString("Packaging product")
            .With("en-US", "Packaging product")
            .With("sv-SE", "Förpackningsprodukt");
        EntityTypeDescription = new LocaleString("Top-level SKU that ships");
        Icon = "package.svg";
        Settings["SyndicationKey"] = "pkg";
    }

    // ---------- Completeness group "Marketing" — sums to 100 on (PackagingProduct, Marketing) ----------

    [DisplayName, FieldNotEmpty(50, typeof(Marketing))]
    public new Field<LocaleString> Name { get; init; } = new() { Mandatory = true };

    [DisplayDescription, FieldNotEmpty(50, typeof(Marketing))]
    public new Field<LocaleString> Description { get; init; } = new();

    // ---------- Completeness group "Quality" — sums to 100 on (PackagingProduct, Quality) ----------

    [ContainsValue(20, typeof(Quality), "SKU-")]
    public Field<string> Sku { get; init; } = new()
    {
        Unique = true,
        Mandatory = true,
        ShowInEntityOverview = true,
        Index = 1,
        RegExp = "^SKU-[A-Z0-9]{4,}$",
        Settings = new(StringComparer.Ordinal) { ["WarehouseHint"] = "barcode" },
    };

    [ExactMatch(20, typeof(Quality), "Paperboard")]
    public Field<string, MaterialFamilyCvl> PrimaryMaterial { get; init; } = new();

    [NumberEvaluation(20, typeof(Quality), NumberEvaluationOperator.GreaterThan, 0)]
    public Field<double> PriceUsd { get; init; } = new()
    {
        Category = typeof(DimensionsCategory),
        Index = 10,
        TrackChanges = true,
    };

    [LinkTypeExists(20, typeof(Quality), typeof(PackagingProductMaterial))]
    [RelationsComplete(20, typeof(Quality))]
    public Field<string> MaterialDossier { get; init; } = new() { Hidden = true };

    // ---------- Completeness group "Logistics" — sums to 100 on (PackagingProduct, Logistics) ----------

    [FieldNotEmpty(100, typeof(Logistics))]
    public Field<double> ShippingWeightGrams { get; init; } = new() { Mandatory = true };

    // ---------- Expression-driven derived fields ----------

    /// <summary>Auto-computed default expression: EUR = USD * 0.85, rounded to 2 decimals.</summary>
    public Field<double> PriceEur { get; init; } = new()
    {
        Category = typeof(DimensionsCategory),
        SupportsExpression = true,
        DefaultExpression = Ex.Round(Ex.FieldValue<double>("PackagingProductPriceUsd") * 0.85, 2),
    };

    public Field<double> LengthMm { get; init; } = new()
    {
        Category = typeof(DimensionsCategory),
        Fieldset = typeof(TechnicalFieldset),
    };

    public Field<double> WidthMm { get; init; } = new()
    {
        Category = typeof(DimensionsCategory),
        Fieldset = typeof(TechnicalFieldset),
    };

    /// <summary>Belongs to two fieldsets — exercises <see cref="Field.Fieldsets"/>.</summary>
    public Field<double> HeightMm { get; init; } = new()
    {
        Category = typeof(DimensionsCategory),
        Fieldsets = new[] { typeof(TechnicalFieldset), typeof(LogisticsFieldset) },
    };

    /// <summary>Locale-aware label.</summary>
    public Field<LocaleString> DimensionsLabel { get; init; } = new()
    {
        Category = typeof(DimensionsCategory),
        SupportsExpression = true,
        DefaultExpression = Ex.LsGenerate(lang =>
            Ex.LsConcatenate(
                Ex.LocaleStringValue("PackagingProductName", "en-US"),
                Ex.Concatenate(" — ", Ex.Text(Ex.FieldValue<double>("PackagingProductLengthMm"))))),
    };

    /// <summary>List-fold expression: sums weights of all linked Material entities for a derived total.</summary>
    public Field<double> TotalMaterialWeight { get; init; } = new()
    {
        Category = typeof(DimensionsCategory),
        SupportsExpression = true,
        DefaultExpression = Ex.Sum<EntityRef>(
            Ex.LinkedEntities("PackagingProductMaterial"),
            v => Ex.FieldValue<double>("MaterialWeightGrams", v)),
    };

    public Field<bool> AvailableInRegion { get; init; } = new() { PerMarket = true };

    [HiddenFor(typeof(Reader))]
    public Field<double> InternalCost { get; init; } = new()
    {
        Category = typeof(DimensionsCategory),
        ReadOnly = true,
        ExcludeFromDefaultView = true,
        TrackChanges = false,
        IgnoreFieldInEpiserverExport = true,
    };

    // ---------- The long tail of Field option coverage ----------

    /// <summary>Multi-value strings — comma-separated on the wire.</summary>
    public Field<string> SearchKeywords { get; init; } = new()
    {
        MultiValue = true,
        Category = typeof(MarketingCategory),
    };

    /// <summary>Multi-row text — exercises <see cref="Field.NumberOfRows"/>.</summary>
    public Field<string> MarketingCopy { get; init; } = new()
    {
        NumberOfRows = 8,
        Category = typeof(MarketingCategory),
    };

    /// <summary>Explicit <see cref="Field.DefaultValue"/> rather than <see cref="Field{TData}.DefaultExpression"/>.</summary>
    public Field<int> WarrantyMonths { get; init; } = new()
    {
        DefaultValue = 12,
        Category = typeof(LegalCategory),
    };

    /// <summary>Bound to a child CVL (CountryCvl has ParentCvl = RegionCvl).</summary>
    public Field<CvlKey, CountryCvl> OriginCountry { get; init; } = new()
    {
        Category = typeof(LegalCategory),
    };

    /// <summary>Bound to the entity-backed Channels CVL.</summary>
    public Field<string, ChannelsCvl> PrimarySalesChannel { get; init; } = new()
    {
        Category = typeof(MarketingCategory),
    };

    /// <summary>Bound to the custom-value-list Brands CVL.</summary>
    public Field<string, BrandsCvl> Brand { get; init; } = new()
    {
        Category = typeof(MarketingCategory),
    };

    /// <summary>Bound to the file-loaded Colours CVL.</summary>
    public Field<string, ColoursCvl> AccentColour { get; init; } = new()
    {
        Category = typeof(MarketingCategory),
    };
}
