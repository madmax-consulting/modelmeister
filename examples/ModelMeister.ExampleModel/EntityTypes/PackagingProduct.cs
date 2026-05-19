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

    [DisplayName, FieldNotEmpty(50, typeof(Marketing)), Mandatory]
    public new Field<LocaleString> Name { get; init; } = new();

    [DisplayDescription, FieldNotEmpty(50, typeof(Marketing))]
    public new Field<LocaleString> Description { get; init; } = new();

    // ---------- Completeness group "Quality" — sums to 100 on (PackagingProduct, Quality) ----------

    [ContainsValue(20, typeof(Quality), "SKU-"), Mandatory, Unique, ShowInEntityOverview, Index(1), RegExp("^SKU-[A-Z0-9]{4,}$")]
    public Field<string> Sku { get; init; } = new()
    {
        Settings = new(StringComparer.Ordinal) { ["WarehouseHint"] = "barcode" },
    };

    [ExactMatch(20, typeof(Quality), "Paperboard")]
    public Field<string, MaterialFamilyCvl> PrimaryMaterial { get; init; } = new();

    [NumberEvaluation(20, typeof(Quality), NumberEvaluationOperator.GreaterThan, 0), FieldCategory(typeof(DimensionsCategory)), Index(10), TrackChanges]
    public Field<double> PriceUsd { get; init; } = new();

    [LinkTypeExists(20, typeof(Quality), typeof(PackagingProductMaterial)), RelationsComplete(20, typeof(Quality)), Hidden]
    public Field<string> MaterialDossier { get; init; } = new();

    // ---------- Completeness group "Logistics" — sums to 100 on (PackagingProduct, Logistics) ----------

    [FieldNotEmpty(100, typeof(Logistics)), Mandatory]
    public Field<double> ShippingWeightGrams { get; init; } = new();

    // ---------- Expression-driven derived fields ----------

    /// <summary>Auto-computed default expression: EUR = USD * 0.85, rounded to 2 decimals.</summary>
    [FieldCategory(typeof(DimensionsCategory)), SupportsExpression]
    public Field<double> PriceEur { get; init; } = new()
    {
        DefaultExpression = Ex.Round(Ex.FieldValue<double>("PackagingProductPriceUsd") * 0.85, 2),
    };

    [FieldCategory(typeof(DimensionsCategory)), Fieldset(typeof(TechnicalFieldset))]
    public Field<double> LengthMm { get; init; } = new();

    [FieldCategory(typeof(DimensionsCategory)), Fieldset(typeof(TechnicalFieldset))]
    public Field<double> WidthMm { get; init; } = new();

    /// <summary>Belongs to two fieldsets — exercises <see cref="Field.Fieldsets"/>.</summary>
    [FieldCategory(typeof(DimensionsCategory)), Fieldset(typeof(TechnicalFieldset)), Fieldset(typeof(LogisticsFieldset))]
    public Field<double> HeightMm { get; init; } = new();

    /// <summary>Locale-aware label.</summary>
    [FieldCategory(typeof(DimensionsCategory)), SupportsExpression]
    public Field<LocaleString> DimensionsLabel { get; init; } = new()
    {
        DefaultExpression = Ex.LsGenerate(lang =>
            Ex.LsConcatenate(
                Ex.LocaleStringValue("PackagingProductName", "en-US"),
                Ex.Concatenate(" — ", Ex.Text(Ex.FieldValue<double>("PackagingProductLengthMm"))))),
    };

    /// <summary>List-fold expression: sums weights of all linked Material entities for a derived total.</summary>
    [FieldCategory(typeof(DimensionsCategory)), SupportsExpression]
    public Field<double> TotalMaterialWeight { get; init; } = new()
    {
        DefaultExpression = Ex.Sum<EntityRef>(
            Ex.LinkedEntities("PackagingProductMaterial"),
            v => Ex.FieldValue<double>("MaterialWeightGrams", v)),
    };

    [PerMarket]
    public Field<bool> AvailableInRegion { get; init; } = new();

    [HiddenFor(typeof(Reader)), FieldCategory(typeof(DimensionsCategory)), ReadOnlyField, IgnoreFieldInEpiserverExport]
    public Field<double> InternalCost { get; init; } = new()
    {
        ExcludeFromDefaultView = true,
        TrackChanges = false,
    };

    // ---------- The long tail of Field option coverage ----------

    /// <summary>Multi-value strings — comma-separated on the wire.</summary>
    [MultiValue, FieldCategory(typeof(MarketingCategory))]
    public Field<string> SearchKeywords { get; init; } = new();

    /// <summary>Multi-row text — exercises <see cref="Field.NumberOfRows"/>.</summary>
    [NumberOfRows(8), FieldCategory(typeof(MarketingCategory))]
    public Field<string> MarketingCopy { get; init; } = new();

    /// <summary>Explicit <see cref="Field.DefaultValue"/> rather than <see cref="Field{TData}.DefaultExpression"/>.</summary>
    [FieldCategory(typeof(LegalCategory))]
    public Field<int> WarrantyMonths { get; init; } = new() { DefaultValue = 12 };

    /// <summary>Bound to a child CVL (CountryCvl has ParentCvl = RegionCvl).</summary>
    [FieldCategory(typeof(LegalCategory))]
    public Field<CvlKey, CountryCvl> OriginCountry { get; init; } = new();

    /// <summary>Bound to the entity-backed Channels CVL.</summary>
    [FieldCategory(typeof(MarketingCategory))]
    public Field<string, ChannelsCvl> PrimarySalesChannel { get; init; } = new();

    /// <summary>Bound to the custom-value-list Brands CVL.</summary>
    [FieldCategory(typeof(MarketingCategory))]
    public Field<string, BrandsCvl> Brand { get; init; } = new();

    /// <summary>Bound to the file-loaded Colours CVL.</summary>
    [FieldCategory(typeof(MarketingCategory))]
    public Field<string, ColoursCvl> AccentColour { get; init; } = new();
}
