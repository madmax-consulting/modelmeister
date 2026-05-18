# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & test

```powershell
dotnet build ModelMeister.sln
dotnet test  ModelMeister.sln
# single test project
dotnet test  tests\ModelMeister.Scaffolder.Tests\ModelMeister.Scaffolder.Tests.csproj
# single test by name
dotnet test  ModelMeister.sln --filter "FullyQualifiedName~Field_CvlId_change"
# try the CLI against the example model
dotnet run --project src\ModelMeister.Cli -- describe --model examples\ModelMeister.ExampleModel\ModelMeister.ExampleModel.csproj
```

All projects target **`net9.0`** via `Directory.Build.props` (flip to net10 once SDK lands). `Nullable`, `TreatWarningsAsErrors`, and `EnforceCodeStyleInBuild` are on globally; central package management is via `Directory.Packages.props` (never put `Version=` on a `PackageReference`).

The Ui project is `net9.0-windows` (Avalonia 11, WinExe).

The `inRiver.Remoting.iPMC` package is published by inriver on their NuGet feed; consumers without access to that feed will need to obtain `inRiver.Remoting.dll` separately and reference it locally.

## Architecture

ModelMeister manages an inriver PIM model as C# code. The data flow:

```
inriver live env  ──[InriverSnapshot]──► LiveModel ─┐
                                                    ├──► ModelDiffer ──► ModelChangeSet ──► ChangeApplier ──► inriver
JSON export ──[InriverModelJson.Load]──► JsonModel  │
       │                                            │
       └──[ProjectScaffolder]──► C# project ──[ModelAssemblyLoader]──► LoadedModel ─┘
```

Five conceptually separate libraries, each living under `src/`:

1. **`ModelMeister.Model`** — the code DSL. No inriver dependency. `EntityType`/`Field<TData>`/`Cvl`/`Category`/`Fieldset`/`LinkType` base types live here. `Field<TData>` records source file + line via `[CallerFilePath]`/`[CallerLineNumber]` so validation errors point at the declaration site. `Loading/ModelLoader` reflects over an assembly and produces a `LoadedModel`.
2. **`ModelMeister.Loading`** — `ModelAssemblyLoader`. Resolves a csproj/dll/dir to an `Assembly`, building csprojs out-of-process via `dotnet build`. Uses a collectible `IsolatedLoadContext` per directory; on reload it `Unload()`s the prior context, then `LoadFromStream`s the bytes (so MSBuild can overwrite the locked DLL on the next build). For older scaffolded projects with a broken `ModelMeister.Model.csproj` ProjectReference, `TryWriteWrapperForBrokenModelReference` emits a temp-dir wrapper csproj that compiles against the bundled Model DLL.
3. **`ModelMeister.Inriver`** — the inriver Remoting glue. `Snapshot/InriverSnapshot` reads top-level concepts in parallel into `LiveModel`. `Mapping/*` converts between `LoadedX`/`LiveX`/inriver DTOs. `Diff/ModelDiffer` is a pure function: `(LoadedModel, LiveModel, MergePolicy) → ModelChangeSet`. `Apply/ChangeApplier` orders changes via a fixed `ApplyOrder` map and pumps them through the remoting client with a receipt + backup. `MergePolicy` gates destructive ops (`AllowDeletes`, `AllowDatatypeChange`, `OverwriteNamesAndDescriptions`, etc.).
4. **`ModelMeister.Scaffolder`** — `JsonModel → C# project`. `ProjectScaffolder.Scaffold` orchestrates `CategoryEmitter`, `CvlEmitter`, `EntityTypeEmitter`, `FieldsetEmitter`, `LinkTypeEmitter`, `RoleEmitter`, `LanguagesEmitter`, `CsprojEmitter`. Bundles the Model DLL under `lib/` of the output so the scaffolded project builds standalone. `BaseClassDetector` extracts shared field-sets into abstract base classes. `LiveModelConverter` adapts a `LiveModel` into the JSON-shaped `InriverModelJson` so the scaffolder can run against either a JSON file or a live env. `ExpressionParser` parses inriver `=...` text into `Expr<T>` C# source.
5. **`ModelMeister.Cli`** + **`ModelMeister.Ui`** — `Spectre.Console` CLI (`System.CommandLine`) and Avalonia desktop. Both go through a `Shell` facade that wraps the libraries.

### Diff/apply contract (read-through semantics)

For nullable code-side properties (`TrackChanges`, `ExcludeFromDefaultView`, `Index`, `DefaultValue`, `Category`, `CvlId`), an unset code value means **"leave inriver's value alone"** rather than "set to default". Both `FieldDiffers` and `FieldTypeMapper.ToInriver` must agree on this — otherwise the diff -> apply -> diff loop is non-idempotent. The mapper's `LiveFieldType? live` parameter is the read-through source.

Idempotency tests at `tests/ModelMeister.Inriver.Tests/IdempotencyDiffTests.cs` pin this — break it and they fail loudly.

### Field bindings via type parameters

Cvl and Category both implement the marker `IFieldBinding` so they can ride in a `Field`'s generic slot:

- `Field<TData>` — plain.
- `Field<TData, TBinding>` where `TBinding : IFieldBinding` — binds either a CVL or a Category; the ctor inspects `typeof(TBinding)` and stamps `Field.Cvl` or `Field.Category` accordingly.
- `Field<TData, TCvl, TCategory>` — when both are needed.

Downstream code (`FieldTypeMapper`, `ModelDiffer`, `ChangeDetails`, `ModelValidator`) reads `Field.Cvl` / `Field.Category` through the **base** property — these are populated by the derived ctor, so the base is authoritative. The category id sent to inriver comes from a `LoadedCategory.CategoryId` lookup keyed by CLR type (built once in the differ and applier), **not** `Type.Name` — categories whose inriver id needed sanitization (`My-Specs` -> class `MySpecs`) would otherwise round-trip wrong.

When the scaffolder emits a Category whose sanitized name collides with an entity-type name (real-world case: both an "ETIM" entity and an "ETIM" category exist), it fully qualifies as `global::{ns}.Categories.X` in the type-param slot to disambiguate from the namespace-local entity type.

## Validation

`ModelValidator` produces `MMxxx`-coded issues, each linked to the declaration's source file + line. Authoritative list with triggers and fixes: `docs/validation-codes.md`. Add a new code there when introducing a new check; tests live alongside (`ValidatorMM024AndMM075Tests.cs` is the shape to follow).

## CI

Three GitHub workflows under `.github/workflows/`: `model-validate.yml` (every PR, exit 3 on validation errors), `model-diff.yml` (every PR against a non-prod env, exit 1 when changes pending — gates merge), `model-apply.yml` (`workflow_dispatch` with env approval). Stable CLI exit codes: 0 success, 1 changes pending, 2 usage error, 3 validation failed, 4 operation failed. Don't change those without coordinating with CI.

## Reference: inriver Remoting surface

Surface dumps so you don't have to crack open the DLL:
- `remoting-surface.txt` — service method signatures
- `remoting-dtos.txt` — DTO property bags

The Remoting client is a process-wide singleton (`RemoteManager.CreateInstance`); `InriverClient` guards it. Retry policy only catches transport-level WCF faults (`CommunicationException` without an inner `FaultException`), `HttpRequestException`, `TimeoutException` — not application errors.
