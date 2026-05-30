# ModelMeister

A C#-first toolkit for managing the **inriver PIM model as code**. You declare entity types,
fields, CVLs, link types, categories, fieldsets, roles, completeness rules and field expressions
in plain C#; ModelMeister diffs that model against any inriver environment and applies the
changes with a backup, a receipt, and a stable exit-code contract suitable for CI.

Two surfaces over the same engine:

- **CLI** (`modelmeister`) — `Spectre.Console` + `System.CommandLine`. Single-purpose flags for CI, a guided menu when run with no args.
- **UI** (`ModelMeister.Ui`) — Avalonia 11 desktop app with credential vault, workflow-shaped pages, and an in-app receipts history.

```
JSON export ─┐                                           ┌─► diff ──► apply ──► inriver
             ├─► scaffold ──► C# model project ──► validate
live env ────┘                                           └─► describe / merge / export
```

## Quickstart

```powershell
# 1. Bootstrap a model project from a live env (or from a JSON export)
modelmeister scaffold --url $env:INRIVER_URL --out .\PimModel --namespace Acme.Pim

# 2. Validate it (static, no env needed)
modelmeister model validate --model .\PimModel\PimModel.csproj

# 3. Preview changes against an environment
modelmeister model diff --model .\PimModel\PimModel.csproj --url $env:INRIVER_URL

# 4. Push them (with backup + receipt)
modelmeister model apply --model .\PimModel\PimModel.csproj --url $env:INRIVER_URL --dry-run
modelmeister model apply --model .\PimModel\PimModel.csproj --url $env:INRIVER_URL --yes
```

Auth is either `--api-key` (or the `INRIVER_API_KEY` env var) or `--username` / `--password`
/ `--environment`. The same options work on every command that talks to inriver.

Running `modelmeister` with no arguments launches an interactive menu — useful while you find your
feet. Prefer the UI? Launch `ModelMeister.Ui` and click through Environments → Model →
Policy → Compare → Apply.

## The flow

ModelMeister is a workflow tool. The pages in the UI mirror the steps the CLI takes:

| Step          | CLI                                                    | UI page         | What happens                                                                                  |
|---------------|--------------------------------------------------------|-----------------|-----------------------------------------------------------------------------------------------|
| Connect       | `--url … --api-key …` on each command                 | Environments    | Pick a saved environment (Windows-DPAPI-encrypted vault) and connect.                         |
| Load model    | `--model <csproj or dll>`                              | Model           | Build (out-of-process) and load the model assembly, then run the static validator.            |
| Set policy    | `--allow-deletes`, `--allow-datatype-change`, …        | Policy          | Decide which destructive operations diff/apply are allowed to consider. Defaults are conservative. |
| Compare       | `diff`                                                 | Compare         | Show what would change. In the UI, skip individual rows to refine the batch.                  |
| Apply         | `apply`                                                | Apply           | Capture a backup, push the changes, write a receipt. Dry-run is the safe rehearsal.           |
| Audit         | (receipts in `.modelmeister/`)                         | History         | Browse receipts and backups, restore a backup by routing it back through Compare.             |

Receipts and backups land next to the model under `.modelmeister/receipts/<env>/` and
`.modelmeister/backups/<env>/`. They're tracked by both CLI and UI and uploaded as workflow
artifacts by the bundled CI pipeline.

## Self-verifying model

Every `Field<TData>` records its source file + line at construction via caller-info attributes —
no stack walking, no `[MethodImpl(NoInlining)]`. Validation errors carry that location:

```
error MM040 Field 'ProductBoxType' references unknown Fieldset 'Packaging.BoxFieldset'
            (at C:\src\Model\EntityTypes\Product.cs:42)
```

Type-level safety on top:

- `Field<TData>` enforces the data type at compile time — `Field<int>` can't accept a `string` default.
- `Field<TData, TCvl>` binds the field to a CVL class; mistyping the CVL name is a compile error.
- `Expr<TData>` lets the compiler type-check expressions; `Ex.Round(Ex.FieldValue<double>("Width") * 0.85, 2)` returns `Expr<double>` and renders to `=ROUND(FIELDVALUE('Width') * 0.85, 2)` at apply time.

Everything else — duplicate ids, display-name uniqueness, link source/target registration,
fieldset binding, completeness sums = 100, language defaults, reserved field ids, expression refs
and cycles — is checked by `ModelValidator`. Each check has a stable `MMxxx` code; the full list
with triggers and fixes is in [`docs/validation-codes.md`](docs/validation-codes.md).

## Authoring a model

```csharp
public abstract class TranslatableEntity : EntityType
{
    [DisplayName]
    public Field<LocaleString> Name { get; init; } = new();

    [DisplayDescription]
    public Field<LocaleString> Description { get; init; } = new();
}

public sealed class Product : TranslatableEntity
{
    public Field<double> PriceUsd { get; init; } = new() { Mandatory = true };

    public Field<double> PriceEur { get; init; } = new()
    {
        SupportsExpression = true,
        DefaultExpression = Ex.Round(Ex.FieldValue<double>("ProductPriceUsd") * 0.85, 2),
    };

    public Field<CvlKey, BrandCvl> Brand { get; init; } = new();
}
```

Inheritance produces `ProductName` and `ProductDescription` field ids automatically — the loader
stamps the concrete entity-type's id on every inherited field. The `BrandCvl` reference is
checked at compile time; the arithmetic on `Expr<double>` is checked at compile time.

Concepts you'll touch:

| Concept                                         | Where it lives                                                  |
|-------------------------------------------------|-----------------------------------------------------------------|
| `EntityType`, `Field<TData>`, `LinkType<…,…>`   | `ModelMeister.Model`                                   |
| `Cvl` (+ `CvlFromEnum<TEnum>`, `CvlFromFile`)   | same — values declared via `GetValues()`                        |
| `Category`, `Fieldset`, `Role`, `Permission`    | same                                                            |
| `CompletenessGroup` + rule attributes           | `…Model.Completeness` (sum to 100 per entity+group)             |
| `SpecificationTemplate`                         | constrained: no completeness rules, no parent-child CVLs        |
| `Ex.*` expression factory                       | `…Model.Expressions`                                            |
| `[Deleted]`, `[IgnoreMigration]`                | `…Model.Lifecycle` — gate destructive operations behind policy  |
| `[HiddenFor]`, `[ReadOnlyFor]`, `[Restricted]`  | `…Model.Security` — per-role field visibility                   |

Reference details for every concept, attribute, and convention live in
[`docs/modelling.md`](docs/modelling.md). A worked example exercising the full surface is at
`examples/ModelMeister.ExampleModel/`.

## Surfaces

### CLI

| Command       | Purpose                                                    | Talks to inriver?   |
|---------------|------------------------------------------------------------|---------------------|
| `scaffold`    | Generate a C# model project from JSON or a live env        | optional            |
| `validate`    | Static validation of a code-defined model                  | no                  |
| `describe`    | Print a summary of a model                                 | no                  |
| `merge`       | Merge two JSON exports with a conflict policy              | no                  |
| `status`      | Connection ping + concept counts                           | yes (read)          |
| `stats`       | Per-entity-type instance counts (`--json` for CI)          | yes (read)          |
| `changes`     | Did the env's model drift since a snapshot/timestamp?      | yes (read)          |
| `diff`        | Show what `apply` would change                             | yes (read)          |
| `apply`       | Push changes, with backup + receipt                        | yes (read + write)  |
| `export`      | Snapshot a live model to JSON                              | yes (read)          |
| `export-xml` / `import-xml` | Lift-and-shift the whole model as inriver-native XML | yes (read / write) |
| `excel`       | Export / import an Excel workbook for the full model       | optional            |
| `cvl`         | Per-CVL workbook + cross-env CVL value sync                | optional            |
| `compare-envs`| Diff two environments (snapshot ↔ live or live ↔ live)     | yes (read)          |
| `users`       | List, export, and provision users                          | yes (read + write)  |
| `extensions`  | List, start, stop, configure inriver Connectors            | yes (read + write)  |
| `interactive` | Spectre.Console menu (default when run with no args)       | optional            |

Full options, examples, and the JSON shapes the CI workflows consume are in
[`docs/cli.md`](docs/cli.md).

### Exit codes

| Code | Meaning                                                                                  |
|-----:|------------------------------------------------------------------------------------------|
| 0    | Success — no work needed                                                                 |
| 1    | Changes pending (`diff --fail-on-changes` or `apply` aborted by user / dry-run)          |
| 2    | Usage / argument error                                                                   |
| 3    | Model validation failed                                                                  |
| 4    | Operation failed (connection error, partial apply)                                       |

These are a public contract — CI workflows rely on them.

### UI

The desktop app wraps the same engine behind a left-rail workflow: **Environments → Model →
Policy → Compare → Apply**, with a **History** page for receipts/backups, a **Tools** page
bundling scaffold/merge/Excel/snapshot utilities, plus dedicated pages for **Compare envs**,
**CVL workbench**, **Users**, and **Extensions**.

Live safety signals run throughout: the **Model** page can overlay live per-entity-type instance
counts and the environment's own icons next to each type; **Compare** can re-check whether the
environment drifted since the snapshot was captured; and the **Apply** confirmation weighs every
destructive change against how many live instances it touches ("clears that value on 48,231 SKU
instances") and warns when the snapshot has gone stale before you commit. The **Tools** page also
exports/imports the whole model as inriver-native XML (lift-and-shift between environments, with an
automatic pre-import backup) and offers one-click environment maintenance (rebuild the quick-search
index, clear the image cache).

Environments hold **two credentials** when the operator wants them: the Remoting API key
(required for model diff/apply) and an optional REST API key (unlocks user creation and the
modern extensions endpoints). Both are stored encrypted per-user (DPAPI) at
`%APPDATA%/ModelMeister/`. Full walkthrough: [`docs/ui.md`](docs/ui.md).

## CI

Three workflows ship under [`.github/workflows/`](.github/workflows/):

1. **`model-validate.yml`** — every PR. Compiles, tests, validates. JSON report in the step summary. Exit `3` on errors.
2. **`model-diff.yml`** — every PR against a non-prod env. Posts the diff in the step summary. Exit `1` when changes are pending, gating the merge.
3. **`model-apply.yml`** — `workflow_dispatch` with environment selection. Dry-run, real apply, then a verification diff that must be empty. Uses GitHub environments for approval on prod.

Per-environment secrets: `INRIVER_URL` and `INRIVER_API_KEY` (or `INRIVER_USERNAME` /
`INRIVER_PASSWORD`). Receipts and backups from CI runs land in `.modelmeister/` and are uploaded
as workflow artifacts.

## Project layout

```
src/
  ModelMeister.Model/         code DSL — no inriver dependency
  ModelMeister.Loading/       ModelAssemblyLoader (builds & loads model csprojs)
  ModelMeister.Inriver/       Remoting client + snapshot + differ + applier + extensions/users/cvl-sync
  ModelMeister.Rest/          REST API client (HttpClient over X-inRiver-APIKey) — user create, extensions
  ModelMeister.Scaffolder/    JSON / live ↔ C# code generator + merger
  ModelMeister.Excel/         Excel workbook export/import (full model + CVL workbook + Users workbook)
  ModelMeister.Cli/           the modelmeister executable
  ModelMeister.Ui/            Avalonia desktop app
examples/
  ModelMeister.ExampleModel/  worked example exercising every concept
tests/
  ModelMeister.Model.Tests/
  ModelMeister.Inriver.Tests/
  ModelMeister.Scaffolder.Tests/
  ModelMeister.Excel.Tests/
docs/
  cli.md            ─ CLI reference
  ui.md             ─ UI guide
  modelling.md      ─ modelling DSL reference
  validation-codes.md ─ every MMxxx code with triggers and fixes
```

## Build & test

```powershell
dotnet build ModelMeister.sln
dotnet test  ModelMeister.sln

# CLI smoke test against the example model:
dotnet run --project src\ModelMeister.Cli -- describe `
  --model examples\ModelMeister.ExampleModel\ModelMeister.ExampleModel.csproj
```

Targets `net9.0` (UI is `net9.0-windows`). `Nullable`, `TreatWarningsAsErrors`, and
`EnforceCodeStyleInBuild` are on globally. Central package management lives in
`Directory.Packages.props` — never put `Version=` on a `PackageReference`.

## Where to next

- Authoring a model → [`docs/modelling.md`](docs/modelling.md)
- CLI reference → [`docs/cli.md`](docs/cli.md)
- UI walkthrough → [`docs/ui.md`](docs/ui.md)
- Validator codes → [`docs/validation-codes.md`](docs/validation-codes.md)

