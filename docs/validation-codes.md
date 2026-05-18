# Model validation codes

`modelmeister validate` emits issues tagged with a stable `MMxxx` code. CI tooling and IDE
integrations can filter on the code; the codes never change meaning across versions.

| Code   | Title                                | What triggers it                                                                                          | How to fix |
| ------ | ------------------------------------ | --------------------------------------------------------------------------------------------------------- | ---------- |
| MM001  | Duplicate EntityType ID              | Two or more `EntityType` subclasses produce the same `EntityTypeId`.                                      | Rename one of the entity types or override `EntityTypeId`. |
| MM002  | Duplicate CVL ID                     | Two CVLs share the same `CvlId`.                                                                          | Rename one of the CVLs. |
| MM003  | Duplicate Category ID                | Two `Category` subclasses share `CategoryId`.                                                             | Rename one. |
| MM004  | Duplicate Fieldset ID                | Two `Fieldset` subclasses share `FieldsetId`.                                                             | Rename one. |
| MM005  | Duplicate LinkType ID                | Two `LinkType` subclasses produce the same `LinkTypeId`.                                                  | Set explicit `LinkTypeId` or differentiate source/target. |
| MM006  | Duplicate Role name                  | Two `Role` subclasses share `Name`.                                                                       | Rename one. |
| MM007  | Duplicate field ID                   | Within one entity type, two `Field` properties collide.                                                   | Rename one of the properties. |
| MM010  | Multiple IsDisplayName               | More than one field on the same entity type has `IsDisplayName = true`.                                   | Pick a single canonical display-name field. |
| MM011  | Multiple IsDisplayDescription        | More than one field has `IsDisplayDescription = true`.                                                    | Pick a single canonical display-description field. |
| MM020  | Unknown CVL type                     | `Field<…, TCvl>` references a CVL class not registered in the model.                                      | Add the CVL to the project or fix the type parameter. |
| MM024  | CVL DataType mismatch                | `Field<TData, TCvl>` where `TData` is incompatible with the CVL's `DataType` (e.g. `double` vs `String`). | Use `Field<CvlKey, TCvl>` or pick a TData matching the CVL's DataType. |
| MM030  | LinkType.Source unknown              | `LinkType.Source` references an entity type not registered.                                               | Register the source entity type. |
| MM031  | LinkType.Target unknown              | `LinkType.Target` references an entity type not registered.                                               | Register the target entity type. |
| MM032  | LinkType.LinkEntityType unknown      | `LinkType.LinkEntityType` references an entity type not registered.                                       | Register the link-entity type. |
| MM040  | Unknown Fieldset type                | `Field.Fieldset(s)` references a fieldset class not registered.                                           | Add the fieldset class to the project. |
| MM041  | Fieldset entity mismatch             | A field references a fieldset that belongs to a different entity type.                                    | Use a fieldset declared for the same entity type. |
| MM050  | Unknown CompletenessGroup            | `[CompletenessRule(Group = typeof(...))]` references a group not registered.                              | Add the `CompletenessGroup` class. |
| MM051  | Completeness weights ≠ 100           | Per-(entity, group) rule weights don't sum to 100.                                                        | Adjust `Weight` values so they total exactly 100. |
| MM060  | No languages declared (warning)      | The model assembly exposes no `Languages.All`.                                                            | Add a `Languages` class exposing at least one `Language`. |
| MM061  | No default language                  | No language has `IsDefault = true`.                                                                       | Mark one language as default. |
| MM062  | Duplicate ISO code                   | Two `Language` entries share `IsoCode`.                                                                   | Remove the duplicate. |
| MM070  | Spec template has completeness field | A `SpecificationTemplate` includes a field that carries a completeness rule — unsupported.                | Remove the rule or remove the field from the template. |
| MM071  | Spec template + child CVL field      | A `SpecificationTemplate` includes a field bound to a parent-child CVL — unsupported.                     | Drop the field from the template, or use a flat CVL. |
| MM075  | PerMarket without resolver           | A field has `PerMarket = true` but no `MarketsCvl` or `IMarketResolver` is registered.                    | Add a `MarketsCvl` subclass or an `IMarketResolver` implementation. |
| MM076  | CVL data file missing                | A `CvlFromFile`-derived CVL's `FilePath` does not exist beside the assembly.                              | Add the file, or update `FilePath`. |
| MM080  | Reserved property name               | A field uses a property name reserved by inriver (`Created`, `Modified`, …).                              | Rename the property. |
| MM090  | Expression on unsupported DataType   | A `DefaultExpression` is set on a field whose Datatype the inriver expression engine doesn't support.     | Drop the expression, or change the field's data type. |
| MM091  | Expression references unknown field  | A `DefaultExpression` references a field ID that doesn't exist.                                           | Fix the field ID or add the missing field. |
| MM092  | Expression references unknown link   | A `DefaultExpression` references a link type ID that doesn't exist.                                       | Fix the link type ID or add the missing link type. |
| MM093  | Expression references unknown CVL value | A `DefaultExpression` references a CVL key not defined in the bound CVL.                               | Fix the CVL key or add the value to the CVL. |
| MM094  | Cyclical expression dependency       | Field A's expression reads field B, which reads field A (directly or transitively).                       | Break the cycle. |

See the [README](../README.md#self-verifying-model) for examples of the kinds of mistakes these
codes catch.
