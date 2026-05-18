# Modelling guide

This is the reference for the code DSL in `ModelMeister.Model` — every base class,
attribute, primitive and convention you'll touch while authoring a C# inriver model. The DSL has
**no inriver dependency**: a model project compiles against this assembly alone. Connecting to a
live environment is a separate concern handled by the CLI and the UI.

## Mental model

```
EntityType      one C# class per inriver entity type
  └─ Field      one property per inriver field
Cvl             one C# class per CVL  (values declared via GetValues())
Category        groups fields within an entity type
Fieldset        named subset view of an entity type's fields
LinkType        directed relationship between two entity types
Role            named permission bundle
CompletenessGroup    a bucket that fields contribute weight to
SpecificationTemplate    curated category + entity-type bundle
Language        ISO codes the model translates into
```

Every concept is **discovered by reflection** (`ModelLoader.LoadFromAssembly`) — concrete,
non-generic, non-abstract subclasses of the relevant base type are picked up automatically. There
is no registry, no `[Register]` attribute, no list to keep in sync. Abstract base classes are
field mixins; only concrete subclasses become inriver entities.

## Identifiers

By default every concept's id comes from the CLR type name, with a conventional suffix stripped:

| Concept       | Suffix(es) stripped         | Override via                              |
|---------------|-----------------------------|--------------------------------------------|
| EntityType    | (none)                      | `EntityTypeId { get; init; }`              |
| Cvl           | `Cvl`                       | `CvlId { get; init; }`                     |
| Category      | `CategoryType`, `Category`  | `CategoryId { get; init; }`                |
| Fieldset      | `Fieldset`                  | `FieldsetId { get; init; }`                |
| LinkType      | (none — defaults to CLR type name) | `LinkTypeId { get; init; }`         |
| Field         | (none — `{EntityId}{Property}`) | `Id { get; init; }`                    |
| Role          | (none)                      | `Name { get; init; }`                      |

`Field` is the special case: ids are stamped by the loader as `{EntityTypeId}{PropertyName}`
because the loader knows which class declares the property. Inheritance walks down to the
deepest declarer (so a property on a base class becomes `ProductName`, not `TranslatableName`).

---

## EntityType

```csharp
public abstract class TranslatableEntity : EntityType
{
    [DisplayName]
    public Field<LocaleString> Name { get; init; } = new() { Mandatory = true };

    [DisplayDescription]
    public Field<LocaleString> Description { get; init; } = new();
}

public sealed class PackagingProduct : TranslatableEntity
{
    public PackagingProduct()
    {
        EntityTypeName = new LocaleString("Packaging product")
            .With("en-US", "Packaging product")
            .With("sv-SE", "Förpackningsprodukt");
        EntityTypeDescription = "Top-level SKU that ships";
        Icon = "package.svg";
        Settings["SyndicationKey"] = "pkg";
    }

    public Field<string> Sku { get; init; } = new() { Unique = true, Mandatory = true };
    // … more fields …
}
```

Init-only members on `EntityType`:

| Member                    | Default                       | Purpose                                                     |
|---------------------------|-------------------------------|-------------------------------------------------------------|
| `EntityTypeId`            | `GetType().Name`              | inriver entity-type id.                                     |
| `EntityTypeName`          | `LocaleString(EntityTypeId)`  | Display label.                                              |
| `EntityTypeDescription`   | empty                         | Localised description.                                      |
| `IsLinkEntityType`        | `false`                       | True for entity types that carry data on a link.            |
| `Icon`                    | `null`                        | Optional icon identifier surfaced in the inriver UI.        |
| `Settings`                | empty dict                    | Free-form per-entity settings forwarded verbatim.           |

**Abstract base classes** are not registered as inriver entities (`ConcreteSubclassesOf<T>`
filters them out) — they exist purely to share field declarations down to concrete subclasses.
`PackagingProduct.Name` inherits from `TranslatableEntity.Name` and is stamped with the concrete
entity-type's id, so the inriver field id becomes `PackagingProductName`.

A property on a derived class with the same name as one on the base **supersedes** the base
declaration entirely (`MaxBy(DepthOfDeclaration)`).

---

## Field

`Field<TData>` is the generic field type. The type argument drives the inriver `Datatype`:

| `TData`                            | inriver `Datatype` |
|------------------------------------|--------------------|
| `string`                           | `String`           |
| `LocaleString`                     | `LocaleString`     |
| `int`, `long`                      | `Integer`          |
| `double`, `decimal`, `float`       | `Double`           |
| `bool`                             | `Boolean`          |
| `DateTime`, `DateTimeOffset`       | `DateTime`         |
| `XElement`, `XmlDocument`          | `Xml`              |
| `CvlKey`                           | `Cvl`              |
| `FileRef`                          | `File`             |

Any other CLR type throws `NotSupportedException` at first use. `decimal`/`float` coerce to
`Double` because inriver always stores doubles on the wire — prefer `double` if exact decimal
semantics matter to your code.

### Construction & source location

```csharp
public Field<string> Sku { get; init; } = new() { Unique = true, Mandatory = true };
```

The `Field<TData>` constructor uses `[CallerFilePath]` / `[CallerLineNumber]` to capture the
declaration site. Validation errors and the UI surface this as a click-through location — there
is no stack-walking. **Don't pass `sourceFile`/`sourceLine` explicitly.**

### Init-only members

| Member                          | Notes                                                                                       |
|---------------------------------|---------------------------------------------------------------------------------------------|
| `Name`                          | Localised display name. Defaults to the property name.                                      |
| `Description`                   | Localised description.                                                                      |
| `DefaultValue`                  | Default value to seed in inriver. Use this **or** `DefaultExpression`, not both.            |
| `DefaultExpression`             | Strongly-typed `Expr<TData>`. See [Expressions](#expressions).                              |
| `SupportsExpression`            | True iff the field accepts an inriver expression as its default.                            |
| `MultiValue`                    | Comma-separated multi-value on the wire.                                                    |
| `Unique`                        | inriver uniqueness constraint.                                                              |
| `Mandatory`                     | Required.                                                                                   |
| `ReadOnly` / `Hidden`           | Global field flags.                                                                         |
| `IsDisplayName`                 | **Prefer the [`[DisplayName]`](#displayname--displaydescription) attribute** on the property. Bool initialiser still works for back-compat. At most one per entity (MM010). |
| `IsDisplayDescription`          | **Prefer the [`[DisplayDescription]`](#displayname--displaydescription) attribute** on the property. At most one per entity (MM011). |
| `ExcludeFromDefaultView`        | Nullable bool. Read-through semantics — `null` leaves inriver's value alone (see below).    |
| `TrackChanges`                  | Nullable bool. Read-through semantics.                                                      |
| `Index`                         | Nullable int. Sort order. Read-through semantics.                                           |
| `RegExp`                        | Regex validating input.                                                                     |
| `NumberOfRows`                  | UI hint for text editors. Default `1`.                                                      |
| `ShowInEntityOverview`          | Show in the inriver entity overview list.                                                   |
| `IgnoreFieldInEpiserverExport`  | Skip this field in the Optimizely connector export.                                         |
| `Category`                      | `Type` of the `Category` class this field belongs to. Set via `Field<TData, TCategory>` or `Category = typeof(…)`. |
| `Cvl`                           | `Type` of the `Cvl` class bound to this field. Set via `Field<TData, TCvl>` or `Cvl = typeof(…)`. |
| `Fieldsets`                     | `IReadOnlyList<Type>` of fieldsets this field appears in.                                   |
| `Fieldset`                      | Convenience setter — assigns a single-element `Fieldsets`. Don't combine with `Fieldsets` on the same initializer. |
| `PerMarket`                     | Field fans out into per-market sibling fields at load time. Requires a `MarketsCvl` or an `IMarketResolver` (MM075). |
| `Settings`                      | Free-form `Dictionary<string,string>` forwarded verbatim.                                   |
| `ReadOnlyFor` / `HiddenFor` / `EditableFor` / `VisibleFor` | `IReadOnlyList<Type>` of `Role` types restricted in the matching way. The attribute form is usually clearer (see [Restrictions](#restrictions)). |

### Read-through semantics

For nullable code-side properties (`TrackChanges`, `ExcludeFromDefaultView`, `Index`,
`DefaultValue`, `Category`, `CvlId`), an unset code value means **"leave inriver's value alone"**
rather than "set to default". This is what makes the diff → apply → diff loop idempotent.

The `FieldTypeMapper` reads the live value through this contract; the diff tests at
`tests/ModelMeister.Inriver.Tests/IdempotencyDiffTests.cs` pin the behaviour.

### Field bindings via type parameters

CVLs and Categories can ride in the generic argument list of `Field`, eliminating the
`Cvl = typeof(…)` / `Category = typeof(…)` initializers. Both base classes implement
`IFieldBinding`:

```csharp
// Plain field
public Field<double> PriceUsd { get; init; } = new();

// Bound to a CVL (TBinding : Cvl) — Cvl property is stamped
public Field<string, BrandsCvl> Brand { get; init; } = new();

// Bound to a Category (TBinding : Category) — Category property is stamped
public Field<double, DimensionsCategory> LengthMm { get; init; } = new();

// Both — needed when a field is CVL-keyed AND assigned to a non-default category
public Field<CvlKey, CountryCvl, LegalCategory> OriginCountry { get; init; } = new();
```

The ctor inspects `typeof(TBinding)` to decide which base property to stamp. Mistyping the bound
class is a compile error — there are no stringly-typed CVL ids in user code.

### CvlKey & FileRef

`Field<CvlKey, TCvl>` is the canonical shape for a CVL-keyed field — `CvlKey` is the phantom
struct that maps to `Datatype.Cvl`. `Field<string, TCvl>` is also valid when the CVL's underlying
`DataType` is `String`; the validator (MM024) cross-checks this.

`FileRef` is the analogous phantom for file fields (`Datatype.File`).

---

## Cvl

Controlled Vocabulary List. The CLR type name (minus a trailing `Cvl` suffix) becomes the id.

```csharp
public sealed class BrandsCvl : Cvl
{
    public override CvlDataType DataType => CvlDataType.String;
    public override bool CustomValueList => true;   // end users may extend in inriver

    public override IEnumerable<CvlValue> GetValues() => new[]
    {
        new CvlValue("Acme", new LocaleString("Acme"), Index: 0),
        new CvlValue("SIG",      new LocaleString("SIG"),       Index: 1),
    };
}
```

`Cvl` members:

| Override          | Default                | Purpose                                                      |
|-------------------|------------------------|--------------------------------------------------------------|
| `CvlId`           | `GetType().Name` minus `Cvl` suffix | inriver CVL id.                                 |
| `DataType`        | `CvlDataType.LocaleString` | One of `String`, `LocaleString`, `Integer`, `Double`, `DateTime`. |
| `ParentCvl`       | `null`                 | Type of a parent CVL — models parent-child CVL hierarchies.  |
| `CustomValueList` | `false`                | True if end users may add values from inriver.               |
| `EntityType`      | `null`                 | Entity type the CVL is keyed by (rare; "entity-backed CVL").  |
| `GetValues()`     | throws                 | The values. Either this or the legacy `Values` property must be overridden — otherwise the loader throws (no silent empty CVLs). |

### `CvlValue`

```csharp
public sealed record CvlValue(
    string Key,
    LocaleString Value,
    string? Parent = null,
    int Index = 0,
    bool Deactivated = false);
```

`Parent` is the CVL key of the parent value when the CVL has a `ParentCvl`. `Deactivated` is the
soft-delete flag.

### Hierarchical CVLs

```csharp
public sealed class RegionCvl : Cvl { /* EU, NA, APAC */ }

public sealed class CountryCvl : Cvl
{
    public override Type? ParentCvl => typeof(RegionCvl);

    public override IEnumerable<CvlValue> GetValues() => new[]
    {
        new CvlValue("SE", new LocaleString("Sweden"),  Parent: "EU", Index: 0),
        new CvlValue("US", new LocaleString("USA"),     Parent: "NA", Index: 2),
    };
}
```

### `CvlFromEnum<TEnum>`

Derive values from an enum. Enum member names become CVL keys.

```csharp
public sealed class PrioritiesCvl : CvlFromEnum<Priority>;
public enum Priority { Low, Normal, High }
```

`DataType` is fixed to `String`. Each value gets `Index = enum-position`.

### `CvlFromFile`

Loads values from a JSON file beside the assembly. The file shape is documented inline in
`Cvls/CvlFromFile.cs`:

```json
[
  { "key": "red",  "value": { "en-US": "Red",  "sv-SE": "Röd" }, "index": 0 },
  { "key": "blue", "value": { "en-US": "Blue", "sv-SE": "Blå" }, "index": 1 }
]
```

```csharp
public sealed class ColoursCvl : CvlFromFile
{
    public override string FilePath => Path.Combine(
        Path.GetDirectoryName(typeof(ColoursCvl).Assembly.Location)!,
        "Cvls", "colours.json");
}
```

Resolution order: absolute path that exists → `AppContext.BaseDirectory + relative` → raw path.
A missing file throws `CvlSourceMissingException` (validation code **MM076**); the CLI surfaces
it as a synthetic validation issue rather than a stack trace.

---

## Category

Visual grouping bucket for fields within an entity type.

```csharp
public sealed class DimensionsCategory : Category
{
    public DimensionsCategory()
    {
        Name = new LocaleString("Dimensions");
    }

    public override int Index => 1;
    public override bool OrderByName => false;
}
```

| Override       | Default                                | Purpose                                                              |
|----------------|----------------------------------------|----------------------------------------------------------------------|
| `CategoryId`   | `GetType().Name` minus `CategoryType`/`Category` suffix | inriver category id.                              |
| `Name`         | `LocaleString(CategoryId)`             | Display name.                                                        |
| `Index`        | `0`                                    | Sort order in the inriver UI.                                        |
| `OrderByName`  | `false`                                | True to alphabetise fields within the category.                      |

Two **reserved** categories are shipped:

- `Categories.General` — index `0`, the ungrouped bucket.
- `Categories.FileInformation` — index `10`, used on the `Resource` entity type.

The loader marks both as `IsReserved`, so diff/apply doesn't try to recreate them.

**Name collisions** — if a category's sanitized name collides with an entity-type name (real-world
example: both an `ETIM` entity and an `ETIM` category), the scaffolder fully-qualifies it
(`global::{ns}.Categories.ETIM`) in the field type-parameter slot to disambiguate. Author-written
code can rename the offending Category class to avoid the collision entirely.

Category ids sent to inriver are looked up by **CLR type**, not by `Type.Name` — categories whose
inriver id needed sanitization (e.g. `My-Specs` → class `MySpecs`) round-trip correctly because
of this.

---

## Fieldset

A named subset view of an entity type's fields. Fields opt in via `Field.Fieldset` /
`Field.Fieldsets`.

```csharp
public sealed class TechnicalFieldset : Fieldset
{
    public override Type EntityType => typeof(PackagingProduct);
    public override int Index => 10;
}
```

| Override       | Default                  | Purpose                                                |
|----------------|--------------------------|--------------------------------------------------------|
| `FieldsetId`   | `GetType().Name` minus `Fieldset` suffix | inriver fieldset id.                |
| `Name`         | `LocaleString(FieldsetId)`               | Display name.                       |
| `Description`  | empty                                    | Localised description.              |
| `EntityType`   | **required**             | CLR type of the owning entity type.                    |
| `Index`        | `0`                                       | Sort order.                                            |

Validator checks:

- **MM040** — A field references a fieldset that isn't in the model.
- **MM041** — A field references a fieldset whose `EntityType` differs from the field's owning entity.

A field can declare multiple fieldsets:

```csharp
public Field<double> HeightMm { get; init; } = new()
{
    Fieldsets = new[] { typeof(TechnicalFieldset), typeof(LogisticsFieldset) },
};
```

`Fieldset = typeof(X)` (singular) is a convenience that replaces `Fieldsets` with a one-element
list — don't combine the two on the same initializer.

---

## LinkType

A directed relationship between two entity types.

```csharp
public sealed class PackagingProductMaterial : LinkType<PackagingProduct, Material>
{
    public override int Index => 0;
}
```

Prefer the generic `LinkType<TSource, TTarget>` over the non-generic base — the constraints
ensure source/target types are entity types and remove the boilerplate.

| Member             | Notes                                                                          |
|--------------------|--------------------------------------------------------------------------------|
| `LinkTypeId`       | Defaults to the CLR type name. The loader falls back to it when no override is supplied — this avoids collisions when multiple link types share endpoints (e.g. `ProductAccessoriesProduct` + `ProductRelatedProduct`, both Product↔Product). |
| `Source` / `Target`| Provided by the generic overload. Override directly on the non-generic base.   |
| `LinkEntityType`   | Optional entity type that carries data **on the link itself** (set `IsLinkEntityType = true` on that entity). |
| `Index`            | Sort order in the inriver UI.                                                  |
| `SourceName` / `TargetName` | Localised labels for the two ends of the link.                        |
| `Settings`         | Free-form settings forwarded verbatim.                                         |

Validator checks: source (MM030), target (MM031), and link-entity-type (MM032) must all be
registered entity types.

---

## Role & Permission

```csharp
public sealed class Editor : Role
{
    public override IReadOnlyList<Type> Permissions => new[]
    {
        typeof(StandardPermissions.View),
        typeof(StandardPermissions.UpdateEntity),
        typeof(StandardPermissions.AddLink),
        typeof(StandardPermissions.UpdateLink),
    };
}
```

`Role` members:

| Member         | Default              | Notes                                                       |
|----------------|----------------------|-------------------------------------------------------------|
| `Name`         | `GetType().Name`     | Role name. Unique across the model (MM006).                 |
| `Description`  | `Name`               | Description.                                                |
| `Permissions`  | empty                | List of `Permission` CLR types granted.                     |

`Permission` is the base; `StandardPermissions` ships every platform permission Remoting 8.21
recognises (View, AddEntity, UpdateEntity, DeleteEntity, AddLink, UpdateLink, DeleteLink,
UpdateCVL, LockEntity, AddFile, AddComments, DeleteComments, PublishChannel, ManageLinkRules,
ContentStore, CopyEntity, InRiverPlanAndRelease, InRiverEnrich, InRiverSupply, InRiverPublish,
InRiverPrint, InRiverCampaignPlanner, Syndicate, SupplierOnboarding, SharePlannerViews,
AdministerSpecificationTemplates, ChangeEntitySegment, ChangeFieldSet, ImportEntitySettings).
You can declare your own custom `Permission` subclasses; they round-trip through the model just
like the standard ones.

### Restrictions

Field-level restrictions are best expressed via attributes on the property:

```csharp
[HiddenFor(typeof(Reader))]
public Field<double> InternalCost { get; init; } = new();

[ReadOnlyFor(typeof(Reader))]
public Field<double> PriceUsd { get; init; } = new();

[Restricted(typeof(Reader), RestrictionType.Hidden)]
public Field<string> AnotherSecret { get; init; } = new();
```

| Attribute               | Effect                                                                                   |
|-------------------------|------------------------------------------------------------------------------------------|
| `[HiddenFor(role)]`     | Hide for the role.                                                                       |
| `[ReadOnlyFor(role)]`   | Read-only for the role.                                                                  |
| `[VisibleFor(role)]`    | Visible-only (not editable) for the role.                                                |
| `[EditOnlyFor(role)]`   | Editable-only (no other access) for the role.                                            |
| `[Restricted(role, RestrictionType)]` | Generic form. `RestrictionType` is `Hidden`, `Readonly`, `Visible`, or `Editonly`. |

All four restriction attributes allow multiple instances per property — so a field can be hidden
for `Reader` and read-only for `Supplier` simultaneously.

Class-level:

```csharp
[RestrictedCategory(typeof(LegalCategory), typeof(Reader), RestrictionType.Hidden,
    EntityTypes = new[] { typeof(PackagingProduct) })]
public sealed class PackagingProduct : TranslatableEntity { … }
```

`[RestrictedCategory]` applies a restriction to every field in a `Category` for a given role,
optionally scoped to a subset of entity types via `EntityTypes`.

---

## Completeness

Each completeness rule contributes weight to a `(EntityType, CompletenessGroup)` pair. The
per-pair sum must be exactly **100** — that's validation code MM051.

```csharp
public sealed class Marketing : CompletenessGroup
{
    public override int Weight => 100;
    public override int SortOrder => 0;
}

public sealed class PackagingProduct : TranslatableEntity
{
    [DisplayName, FieldNotEmpty(50, typeof(Marketing))]
    public new Field<LocaleString> Name { get; init; } = new();

    [DisplayDescription, FieldNotEmpty(50, typeof(Marketing))]
    public new Field<LocaleString> Description { get; init; } = new();

    // (PackagingProduct, Marketing) sum: 50 + 50 = 100  ✓
}
```

### Rule attributes

All extend `CompletenessRuleAttribute(int weight, Type group)`. Apply multiple per property.

| Attribute                                                     | Semantics                                                       |
|---------------------------------------------------------------|-----------------------------------------------------------------|
| `[FieldNotEmpty(weight, group, note?)]`                       | Complete when the field has any non-empty value.                |
| `[ContainsValue(weight, group, value)]`                       | Complete when the field's value contains `value`.               |
| `[ExactMatch(weight, group, expected)]`                       | Complete when the field exactly matches `expected`.             |
| `[LinkTypeExists(weight, group, linkType)]`                   | Complete when ≥1 link of `linkType` exists.                     |
| `[RelationsComplete(weight, group)]`                          | Complete when all related entities themselves report complete.  |
| `[NumberEvaluation(weight, group, operator, value)]`          | Complete when the numeric value satisfies the comparison.       |

`NumberEvaluationOperator`: `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`,
`LessThanOrEqual`.

`CompletenessGroup` members:

| Member        | Default                  | Notes                                                  |
|---------------|--------------------------|--------------------------------------------------------|
| `Name`        | `LocaleString(GetType().Name)` | Display name.                                    |
| `Weight`      | `0`                      | Informational total weight (per-rule weights are enforced). |
| `SortOrder`   | `0`                      | Display sort order.                                    |

Validator checks: **MM050** for unknown groups, **MM051** for per-pair sums ≠ 100.

---

## SpecificationTemplate

A curated view bound to a set of categories and entity types — inriver renders it as a "spec
sheet".

```csharp
public sealed class PackagingProductSpec : SpecificationTemplate
{
    public PackagingProductSpec()
    {
        Name = new LocaleString("Packaging spec sheet");
        Description = "Per-SKU spec sheet bound to ProductSpec";
    }

    public override IReadOnlyList<Type> Categories => new[]
    {
        typeof(MarketingCategory),
        typeof(LegalCategory),
    };

    public override IReadOnlyList<Type> EntityTypes => new[] { typeof(ProductSpec) };
}
```

Two important constraints — both enforced statically:

- **MM070** — spec templates cannot include fields that carry completeness rules.
- **MM071** — spec templates cannot include fields bound to a parent-child CVL.

The usual pattern is to bind the template to a dedicated "spec" entity type that has none of
these features; the example model's `ProductSpec` is exactly that.

---

## Markets & PerMarket fields

`Field.PerMarket = true` fans the field out into per-market sibling fields at load time. The set
of markets comes from an `IMarketResolver`:

```csharp
public interface IMarketResolver
{
    IReadOnlyDictionary<string, string> GetMarkets();   // key -> display label
}
```

The default resolver, `CvlMarketResolver`, discovers a concrete `MarketsCvl` subclass in the
model assembly and emits its CVL values as the market list:

```csharp
public sealed class MarketsCvl : Model.Markets.MarketsCvl
{
    public override IEnumerable<CvlValue> GetValues() => new[]
    {
        new CvlValue("EU",   "Europe",        Index: 0),
        new CvlValue("NA",   "North America", Index: 1),
        new CvlValue("APAC", "Asia Pacific",  Index: 2),
    };
}

// then
public Field<bool> AvailableInRegion { get; init; } = new() { PerMarket = true };
```

Validator code **MM075** flags `PerMarket = true` when no `MarketsCvl` or `IMarketResolver` is
registered.

---

## Languages

The model exposes its set of languages via a static collection. The loader discovers any
`public static` property or field whose type is assignable to `IEnumerable<Language>` — convention
is `Languages.All`.

```csharp
public static class Languages
{
    public static IEnumerable<Language> All { get; } = new[]
    {
        new Language("en-US", IsDefault: true),
        new Language("sv-SE"),
        new Language("de-DE"),
        new Language("ja-JP"),
    };
}
```

Validator checks:

- **MM060** — warning when no languages are declared (the model still loads).
- **MM061** — error when no language has `IsDefault = true`.
- **MM062** — error on duplicate ISO codes.

`LocaleString` lookups are case-insensitive on the ISO code.

---

## Expressions

`Field<TData>.DefaultExpression` accepts an `Expr<TData>` — a strongly-typed expression tree that
renders to inriver's `=…` expression text at apply time. Set `SupportsExpression = true` on the
field so the differ knows to emit the expression.

```csharp
public Field<double> PriceEur { get; init; } = new()
{
    SupportsExpression = true,
    DefaultExpression = Ex.Round(Ex.FieldValue<double>("PackagingProductPriceUsd") * 0.85, 2),
};
```

Renders to `=ROUND(FIELDVALUE('PackagingProductPriceUsd') * 0.85, 2)`.

### The `Ex` factory

Every inriver Expression-Engine function has a strongly-typed helper in
`ModelMeister.Model.Expressions.Ex` (one method per `docs/expression-engine.txt` entry).
Grouped by family:

- **Math** — `Abs`, `Average`, `Ceiling`, `Convert(value, fromUnit, toUnit)` (units validated
  against a whitelist), `Floor`, `Max`, `Min`, `Pi`, `Power`, `Rand`, `RandBetween`, `Round`,
  `RoundUp`, `RoundDown`, `Sqrt`.
- **DateTime** — `DateTime(year, month?, day?, …)`, `DateTimeDif(a, b, unit)`,
  `DateTimeAdd(dt, unit, amount)`, `EvaluationDateTime(timezone?)`.
  `DateTimeUnit`: `Years`, `Months`, `Days`, `Hours`, `Minutes`, `Seconds`.
- **LocaleString** — `LocaleString(pairs)`, `LocaleStringValue(fieldId, lang, entity?)`,
  `LsConcatenate`, `LsLeft`, `LsRight`, `LsUpper`, `LsLower`, `LsGenerate(body)` (closure
  over `$LANG`), `LsExtract`, `LsRegexReplace`.
- **String** — `Char`, `Concatenate`, `S(literal)` (lift to `Expr<string>`), `Len`, `Left`,
  `Right`, `Number`, `Text`, `TextJoin`, `TextSplit`, `RegexTest`, `RegexExtract`, `RegexReplace`,
  `Replace`, `Search`, `Substitute`, `Trim`, `Proper`, `Upper`, `Lower`, `XmlValue`, `JsonValue`.
- **List / iteration** — `List`, `LinkedEntities`, `Any`, `All`, `First`, `Count`, `Sum` (and the
  projecting overload), `SumIf`, `Map`, `Filter`, `Distinct`. The lambda-shaped helpers bind a
  `$VALUE` `VarExpr<T>` you receive as a typed argument.
- **Logic** — `If`, `Ifs(cases)`, `Switch(subject, cases, default)`, `And`, `Or`, `Not`,
  `IsError`, `IsNumber`, `IsEmpty`, `Guid`.
- **Inriver-specific** — `FirstLinkedEntity`, `FieldValue<T>(id, entity?)`,
  `FieldValues(currentFieldsetOnly, categoryId?)`, `FieldSetId(entity?)`,
  `CvlValue(cvlId, key)`, `FieldCvlValue(fieldId, entity?)`, `SegmentId(entity?)`,
  `SegmentName(segmentId?)`.
- **Escape hatch** — `Ex.Raw<T>("…")` emits arbitrary text as `Expr<T>`. The scaffolder uses this
  for `=…` expressions it can't parse, accompanied by a `warn:` so authors can review.

### Composition

`Expr<T>` overloads `+ - * /` for numerics and the comparison operators; literals lift via
implicit conversions. Result types are correctly propagated:

```csharp
Ex.FieldValue<double>("Width") * 0.85           // Expr<double>
Ex.Len(Ex.FieldValue<string>("Sku")) > 4        // Expr<bool>
```

### List folds with lambdas

```csharp
public Field<double> TotalMaterialWeight { get; init; } = new()
{
    SupportsExpression = true,
    DefaultExpression = Ex.Sum<EntityRef>(
        Ex.LinkedEntities("PackagingProductMaterial"),
        v => Ex.FieldValue<double>("MaterialWeightGrams", v)),
};
```

`Ex.Sum<T>(list, projection)`, `Ex.SumIf`, `Ex.Map`, `Ex.Filter`, `Ex.Any`, `Ex.All`, `Ex.First`,
`Ex.Count` all bind a `$VALUE` variable into your lambda. You pass that variable into other `Ex.*`
calls — the inriver renderer emits the right `$VALUE.field` reference.

### LSGENERATE closure

```csharp
Ex.LsGenerate(lang =>
    Ex.LsConcatenate(
        Ex.LocaleStringValue("PackagingProductName", "en-US"),
        Ex.Concatenate(" — ", Ex.Text(Ex.FieldValue<double>("PackagingProductLengthMm")))))
```

`lang` is an `Expr<string>` bound to inriver's `$LANG` variable inside the body.

### Validation

The expression walker recurses through every `DefaultExpression` and surfaces:

- **MM090** — Expression on an unsupported datatype (`Xml`, `File`).
- **MM091** — `FieldValue` / `FieldCvlValue` references an unknown field id.
- **MM092** — `LinkedEntities` / `FirstLinkedEntity` references an unknown link-type id.
- **MM093** — `CvlValue` references a key not present in the bound CVL.
- **MM094** — Cycle detected (A reads B reads A).

Spec templates also reject fields with expressions — MM070 / MM071 cover the related cases.

---

## Lifecycle attributes

```csharp
[Deleted]
public sealed class LegacyProduct : EntityType { … }

[Deleted]
public Field<string> ObsoleteSku { get; init; } = new();

[IgnoreMigration]
public Field<int> RetypedField { get; init; } = new();
```

| Attribute            | Applies to         | Effect                                                                                            |
|----------------------|--------------------|---------------------------------------------------------------------------------------------------|
| `[Deleted]`          | class or property  | Diff emits a Delete change for the target — but **only when `--allow-deletes`** is set. Otherwise the differ skips it with a warning. |
| `[IgnoreMigration]`  | property           | Skip the field from datatype-migration logic. Use when you're migrating data out-of-band and don't want the applier to attempt the destructive datatype change. |

Both are picked up automatically (`LoadedField.MarkedForDeletion`, `LoadedEntityType.MarkedForDeletion`).

---

## Reserved names

The validator (MM080) rejects field property names that collide with inriver-reserved field ids
(`Created`, `Modified`, etc.). Pick a different property name — the type-name + property
convention gives you `ProductCreated` if you really need to track a `Created` semantic.

---

## Validation in summary

Run `modelmeister validate --model MyModel.csproj` (or use the Model page in the UI). The full
list of codes — every trigger and how to fix — is in [`validation-codes.md`](validation-codes.md).
Each issue carries a stable `MMxxx` code and (where applicable) the source-file + line of the
declaration site, so IDE click-through navigates to the bad code immediately.

```
error MM040 Field 'ProductBoxType' references unknown Fieldset 'Packaging.BoxFieldset'
            (at C:\src\Model\EntityTypes\Product.cs:42)
```

## Authoring conventions

A few patterns that recur through a healthy model:

- **Shared display fields** — put `Name` / `Description` on an abstract base (the example uses
  `TranslatableEntity`). Concrete entity types inherit them and get correctly-prefixed field ids.
- **`init` everywhere** — every concept is constructed once at load time; mutable state buys you
  nothing and risks order-of-initialisation surprises.
- **Use the typed `Field<…,…>` overloads** — prefer `Field<string, BrandsCvl>` over
  `Field<string> { Cvl = typeof(BrandsCvl) }`. Compile-time errors beat MM020 every time.
- **Settings dictionaries are verbatim** — `EntityType.Settings`, `Field.Settings`,
  `LinkType.Settings` round-trip to inriver unchanged. Use them for syndication keys and other
  free-form metadata that has no first-class field.
- **Don't fight read-through** — leave `Index`, `TrackChanges`, `ExcludeFromDefaultView` unset
  unless you specifically want to manage them from code. Setting them forces the diff to fight
  with inriver's own value.
- **Keep spec templates simple** — they cannot contain completeness rules (MM070) or parent-child
  CVL fields (MM071). Spec entity types are the natural home for those constraints.

The example project at `examples/ModelMeister.ExampleModel/` exercises every concept and
attribute in this guide and builds clean — read it alongside this doc as a worked reference.

## `[DisplayName]` / `[DisplayDescription]`

These property-level attributes are the preferred way to mark a field as an entity's display name
or display description. They keep the field initialiser focused on data and pull the "this field
is the display name" decision next to the property where readers expect it.

```csharp
public abstract class TranslatableEntity : EntityType
{
    [DisplayName]
    public Field<LocaleString> Name { get; init; } = new() { Mandatory = true };

    [DisplayDescription]
    public Field<LocaleString> Description { get; init; } = new();
}
```

Constraints:

- At most one `[DisplayName]` per concrete entity type (MM010).
- At most one `[DisplayDescription]` per concrete entity type (MM011).
- The bool initialiser form (`IsDisplayName = true`, `IsDisplayDescription = true`) still works
  for back-compat — attributes win when both are present.

## Shorthand helpers (`Mm.*`, `FieldEx.*`)

`ModelMeister.Model` ships a small set of helpers that remove repetition without hiding
intent. None of them invent new behaviour — anything you write with the shorthand can also be
written longhand.

### Typed field references

`Mm.Field<TEntity>(e => e.Property)` returns the field id without a string literal — handy when
an expression or completeness attribute needs to reference a field by id:

```csharp
var nameId = Mm.Field<Product>(p => p.Name);  // -> "ProductName"
```

### LocaleString construction

```csharp
Mm.Loc(("en", "Brand"), ("sv", "Varumärke"));   // multi-lang
Mm.L("Brand");                                   // single-lang shorthand
```

### Fluent field helpers

The `FieldEx` extension class adds five small helpers for common field flags:

```csharp
public Field<double> Price { get; init; } = new Field<double>()
    .Required()       // Mandatory = true
    .UniqueValue()    // Unique = true
    .At(3)            // Index = 3
    .In(typeof(MarketingFieldset), typeof(PricingFieldset));
```

Mix-and-match with object-initialisers freely; the helpers are sugar over the same init-only
properties.
