using ModelMeister.ExampleModel.Cvls;
using ModelMeister.Model;
using ModelMeister.Model.Expressions;
using ModelMeister.Model.Primitives;

namespace ModelMeister.ExampleModel.EntityTypes;

/// <summary>
/// Spec-sheet entity used by <see cref="SpecificationTemplates.PackagingProductSpec"/>.
/// Deliberately carries NO completeness rules and NO parent-child CVL bindings so it stays
/// MM070/MM071-clean.
/// Doubles as the playground for the long tail of <see cref="Ex"/> expression helpers.
/// </summary>
public sealed class ProductSpec : EntityType
{
    [DisplayName]
    public Field<string> SpecKey { get; init; } = new() { Unique = true };

    public Field<string, BrandsCvl> Brand { get; init; } = new();

    public Field<int, PrioritiesCvl> Priority { get; init; } = new();

    public Field<double, TaxRatesCvl> TaxRate { get; init; } = new();

    public Field<DateTime, ReleaseDatesCvl> ReleaseSlot { get; init; } = new();

    // --- Math expressions ---

    /// <summary>ABS / AVERAGE / CEILING / FLOOR / MAX / MIN / POWER / SQRT / ROUNDUP / ROUNDDOWN exercised here.</summary>
    public Field<double> VolumeCm3 { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression =
            Ex.RoundUp(
                Ex.Max(
                    Ex.Min(
                        Ex.Ceiling(Ex.FieldValue<double>("PackagingProductLengthMm") / 10.0, 1.0),
                        Ex.Floor(Ex.FieldValue<double>("PackagingProductWidthMm") / 10.0, 1.0)),
                    Ex.Abs(Ex.FieldValue<double>("PackagingProductHeightMm") / 10.0)),
                0),
    };

    public Field<double> SphericalVolume { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression =
            (Ex.Pi * Ex.Power(Ex.FieldValue<double>("PackagingProductLengthMm") / 2.0, 3)) * (4.0 / 3.0),
    };

    public Field<double> AveragePrice { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Average(
            Ex.FieldValue<double>("PackagingProductPriceUsd"),
            Ex.FieldValue<double>("PackagingProductPriceEur")),
    };

    public Field<double> DiagonalMm { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.RoundDown(
            Ex.Sqrt(
                Ex.Power(Ex.FieldValue<double>("PackagingProductLengthMm"), 2)
                + Ex.Power(Ex.FieldValue<double>("PackagingProductWidthMm"), 2)
                + Ex.Power(Ex.FieldValue<double>("PackagingProductHeightMm"), 2)),
            2),
    };

    /// <summary>CONVERT — unit conversion. Demonstrates the unit-allowlist gate in <see cref="Ex.Convert"/>.</summary>
    public Field<double> LengthInches { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Convert(
            Ex.FieldValue<double>("PackagingProductLengthMm") / 1000.0,
            fromUnit: "m",
            toUnit: "in"),
    };

    /// <summary>RAND / RANDBETWEEN — non-deterministic; here to lock in the surface.</summary>
    public Field<double> Lottery { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Rand() + Ex.RandBetween(1, 100),
    };

    // --- DateTime expressions ---

    public Field<DateTime> ReleaseDate { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.DateTimeAdd(
            Ex.DateTime(2025, 1, 1),
            DateTimeUnit.Months,
            6),
    };

    public Field<double> DaysSinceEpoch { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.DateTimeDif(
            Ex.DateTime(1970, 1, 1),
            Ex.EvaluationDateTime("UTC"),
            DateTimeUnit.Days),
    };

    // --- LocaleString expressions ---

    public Field<LocaleString> Marketing { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.LsRegexReplace(
            Ex.LsUpper(
                Ex.LsLeft(
                    Ex.LsLower(
                        Ex.LsRight(
                            Ex.LsConcatenate(
                                Ex.LocaleString(("en-US", "Promo"), ("sv-SE", "Kampanj")),
                                Ex.FieldValue<LocaleString>("PackagingProductName")),
                            20)),
                    50)),
            pattern: "[^A-Za-z ]",
            replacement: ""),
    };

    public Field<string> EnglishMarketing { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.LsExtract(Ex.FieldValue<LocaleString>("PackagingProductName"), "en-US"),
    };

    /// <summary>LSGENERATE — per-language closure that reads $LANG and emits a LocaleString.</summary>
    public Field<LocaleString> PerLanguageHeader { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.LsGenerate(lang =>
            Ex.LsConcatenate(
                Ex.LocaleString(("en-US", "Header: "), ("sv-SE", "Rubrik: ")),
                Ex.FieldValue<LocaleString>("PackagingProductName"))),
    };

    // --- String expressions ---

    public Field<string> RenderedSku { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.TextJoin(
            delimiter: "-",
            ignoreEmpty: true,
            Ex.Upper(Ex.FieldValue<string>("PackagingProductSku")),
            Ex.Proper(Ex.Trim(Ex.FieldValue<string>("PackagingProductPrimaryMaterial"))),
            Ex.S("v1")),
    };

    public Field<string> SkuPrefix { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Substitute(
            Ex.Left(Ex.FieldValue<string>("PackagingProductSku"), 6),
            Ex.S("_"),
            Ex.S("-"),
            instance: 1),
    };

    public Field<string> SkuSuffix { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Right(Ex.FieldValue<string>("PackagingProductSku"), 4),
    };

    public Field<int> SkuLength { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Len(Ex.FieldValue<string>("PackagingProductSku")),
    };

    public Field<int> NeedleIndex { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Search(Ex.S("-"), Ex.FieldValue<string>("PackagingProductSku")),
    };

    public Field<string> ReplacedSku { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Replace(
            Ex.FieldValue<string>("PackagingProductSku"),
            start: 1, count: 3,
            newText: Ex.S("NEW")),
    };

    public Field<string> NormalisedSku { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Lower(
            Ex.RegexReplace(
                Ex.FieldValue<string>("PackagingProductSku"),
                pattern: "[^A-Za-z0-9]+",
                replacement: "_")),
    };

    public Field<string> FirstDigits { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.RegexExtract(
            Ex.FieldValue<string>("PackagingProductSku"),
            pattern: "[0-9]+",
            returnMode: 0),
    };

    public Field<bool> SkuLooksNumeric { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.RegexTest(Ex.FieldValue<string>("PackagingProductSku"), pattern: "^[0-9]+$"),
    };

    public Field<double> SkuAsNumber { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Number(Ex.FieldValue<string>("PackagingProductSku")),
    };

    public Field<string> NewlineSeparator { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Char(10),
    };

    /// <summary>JSONVALUE / XMLVALUE — extract a value from a string-encoded blob.</summary>
    public Field<string> ExifMake { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.XmlValue(Ex.Text(Ex.FieldValue<string>("PackagingProductSku")), xpath: "//Make"),
    };

    public Field<string> JsonProbe { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.JsonValue(Ex.FieldValue<string>("PackagingProductSku"), jsonPath: "$.brand"),
    };

    // --- List / fold expressions ---

    /// <summary>FILTER / MAP / DISTINCT / COUNT / ANY / ALL / FIRST / SUMIF — list-shaping suite.</summary>
    public Field<int> MaterialCount { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Count<EntityRef>(
            Ex.LinkedEntities("PackagingProductMaterial"),
            v => Ex.FieldValue<double>("MaterialWeightGrams", v) > 0.0),
    };

    public Field<bool> AnyMaterialHeavy { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Any<EntityRef>(
            Ex.LinkedEntities("PackagingProductMaterial"),
            v => Ex.FieldValue<double>("MaterialWeightGrams", v) > 1000.0),
    };

    public Field<bool> AllMaterialsHavePositiveWeight { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.All<EntityRef>(
            Ex.LinkedEntities("PackagingProductMaterial"),
            v => Ex.FieldValue<double>("MaterialWeightGrams", v) > 0.0),
    };

    public Field<double> HeaviestMaterialWeight { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.FieldValue<double>(
            "MaterialWeightGrams",
            Ex.First<EntityRef>(
                Ex.LinkedEntities("PackagingProductMaterial"),
                v => Ex.FieldValue<double>("MaterialWeightGrams", v) > 100.0)),
    };

    public Field<double> WeightOfHeavyMaterials { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.SumIf<EntityRef>(
            Ex.LinkedEntities("PackagingProductMaterial"),
            condition: v => Ex.FieldValue<double>("MaterialWeightGrams", v) > 50.0,
            projection: v => Ex.FieldValue<double>("MaterialWeightGrams", v)),
    };

    public Field<int> DistinctMaterialFamilies { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Count<string>(
            Ex.Distinct(
                Ex.Map<EntityRef, string>(
                    Ex.Filter<EntityRef>(
                        Ex.LinkedEntities("PackagingProductMaterial"),
                        v => Ex.FieldValue<double>("MaterialWeightGrams", v) > 0.0),
                    v => Ex.FieldValue<string>("MaterialFamily", v)))),
    };

    public Field<double> SumOfDoubles { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Sum(Ex.List<double>(1.5, 2.5, 3.5)),
    };

    public Field<int> RawListLength { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Count(Ex.List<string>("a", "b", "c")),
    };

    public Field<string> SplitFirst { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.First<string>(
            Ex.TextSplit(Ex.FieldValue<string>("PackagingProductSku"), delimiter: "-", ignoreEmpty: true)),
    };

    // --- Logic / branching ---

    public Field<string> Tier { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Ifs(
            (Ex.FieldValue<double>("PackagingProductPriceUsd") > 1000.0, Ex.S("premium")),
            (Ex.FieldValue<double>("PackagingProductPriceUsd") > 100.0,  Ex.S("standard")),
            (Expr<double>.Eq(Ex.FieldValue<double>("PackagingProductPriceUsd"), 0.0), Ex.S("free")),
            ((Expr<bool>)true, Ex.S("entry"))),
    };

    public Field<string> SimpleIf { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.If(
            Ex.And(
                Ex.FieldValue<double>("PackagingProductPriceUsd") > 0.0,
                Ex.Or(
                    Ex.Not(Ex.IsEmpty(Ex.FieldValue<string>("PackagingProductSku"))),
                    Ex.IsNumber(Ex.FieldValue<double>("PackagingProductPriceEur")))),
            Ex.S("valid"),
            Ex.S("invalid")),
    };

    public Field<string> SwitchOnFamily { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Switch(
            Ex.FieldValue<string>("PackagingProductPrimaryMaterial"),
            cases: new[]
            {
                ((Expr<string>)"Paperboard", (Expr<string>)"paper"),
                ((Expr<string>)"Aluminium",  (Expr<string>)"metal"),
                ((Expr<string>)"Polymer",    (Expr<string>)"plastic"),
            },
            defaultValue: Ex.S("other")),
    };

    public Field<bool> IsValid { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Not(Ex.IsError(Ex.FieldValue<double>("PackagingProductPriceUsd"))),
    };

    public Field<string> NewGuid { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Guid(),
    };

    // --- Inriver-specific ---

    public Field<string> CurrentFieldSet { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.FieldSetId(),
    };

    public Field<string> CurrentSegment { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.SegmentName(Ex.SegmentId()),
    };

    public Field<LocaleString> FirstMaterialFamilyLabel { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.FieldCvlValue(
            "MaterialFamily",
            Ex.FirstLinkedEntity("PackagingProductMaterial")),
    };

    /// <summary>CVLVALUE — direct lookup. Both args (cvlId, key) must resolve at validation time.</summary>
    public Field<LocaleString> MarketsEuropeLabel { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.CvlValue("Markets", "EU"),
    };

    public Field<string> RawEscapeHatch { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Raw<string>("=SOME_FUTURE_FUNCTION('arg')"),
    };
}
