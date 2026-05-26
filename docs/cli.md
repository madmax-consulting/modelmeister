# CLI reference

`modelmeister` is the single executable produced by `ModelMeister.Cli`. Running it with no
arguments drops into an interactive Spectre.Console menu; otherwise it is a standard
`System.CommandLine` host organised into noun-verb subcommand groups.

```
modelmeister                          # interactive menu (no args)
modelmeister <group> <verb> [options]
modelmeister --version                # print version
modelmeister --no-color <command>     # disable ANSI colours (also: NO_COLOR env var)
modelmeister workflows                # cheat-sheet of common command sequences
```

Every command supports `--help`, which prints the description, options, **Examples**, and
**See also** sections.

## Command tree

```
model       validate · describe · diff · apply
env         status · snapshot · compare
scaffold
excel       export · import
cvl         export · import · sync
json        merge
users       list · template · provision
extensions  list · start · stop · logs · set
workflows
```

## Exit codes

These are a stable contract — `.github/workflows/*.yml` consume them directly. Don't change
the values without coordinating with the workflows.

| Code | Constant            | Meaning                                                                   |
|-----:|---------------------|---------------------------------------------------------------------------|
| 0    | `Success`           | Operation succeeded.                                                      |
| 1    | `ChangesPending`    | `model diff --fail-on-changes` found changes, or `model apply` was aborted / dry-run. |
| 2    | `UsageError`        | Missing, conflicting, or invalid arguments.                               |
| 3    | `ValidationFailed`  | `model validate` produced at least one error issue.                       |
| 4    | `OperationFailed`   | Connection error, IO error, or partial apply.                             |

See `src/ModelMeister.Cli/ExitCodes.cs` for the canonical definition.

## Connection options

Every command that talks to inriver accepts the same options (declared once in
`ConnectionOptions.cs`):

| Option         | Purpose                                                          |
|----------------|------------------------------------------------------------------|
| `--url`        | inriver Remoting URL. Required for connected commands.           |
| `--api-key`    | API key. Falls back to the `INRIVER_API_KEY` env var.            |
| `--username`   | Username for legacy credential-based auth.                       |
| `--password`   | Password.                                                        |
| `--environment`| Optional inriver environment name (used with username/password). |

Either an API key (explicit or via `INRIVER_API_KEY`) **or** a username/password pair is required.
Without one of those, connected commands return `2 UsageError`.

## Source picker

Commands that accept multiple input sources (`scaffold`, `excel export`, `cvl export`) use a
consistent flag set. Pass exactly one — both, or neither, returns `2 UsageError`:

| Flag      | Meaning                                                            |
|-----------|--------------------------------------------------------------------|
| `--json`  | Path to an inriver export JSON.                                    |
| `--excel` | Path to an Excel workbook. `--xlsx` is accepted as an alias.       |
| `--model` | Path to a C# model project (csproj, built dll, or directory).      |
| `--url`   | Live inriver URL (uses the standard auth options).                 |

---

## `model`

Operations on a code-defined model.

```
modelmeister model validate --model <project>            [--json]
modelmeister model describe --model <project>            [--json]
modelmeister model diff     --model <project> --url <U>  [--format tree|text|json]
                                                         [--out <file>] [--fail-on-changes]
                                                         [--allow-deletes] [--allow-datatype-change]
modelmeister model apply    --model <project> --url <U>  [--yes] [--dry-run]
                                                         [--allow-deletes] [--allow-datatype-change]
                                                         [--allow-cvl-value-rename]
```

- `model validate` catches every `MMxxx` issue in [`validation-codes.md`](validation-codes.md).
  Exits `0` on clean, `3` on at least one error (warnings are allowed), `4` on load failure.
- `model diff` reports what `apply` would change. Without `--fail-on-changes` it always exits `0`;
  with it, `1` when changes are pending — that's the CI-gate signal.
- `model apply` captures a backup snapshot to `.modelmeister/backups/<env>/…` before any writes
  and a receipt to `.modelmeister/receipts/<env>/…` afterwards. Exits `4` if any change failed.
  `--yes` is required for CI.

## `env`

Operations against a live inriver environment.

```
modelmeister env status   --url <U>
modelmeister env snapshot --url <U> --out <file.json>
modelmeister env compare  --left-url <U1>  --right-url <U2> [--format text|json] [--out <file>]
modelmeister env compare  --left-json <f>  --right-url <U2>
```

- `env status` is a connectivity smoke-test that prints concept counts.
- `env snapshot` captures a live model to JSON (formerly `export`). The JSON is the canonical
  shape consumed by `scaffold --json`, `json merge`, and `cvl sync`.
- `env compare` compares two environments. The left side may be a saved snapshot OR a live URL;
  the right side is always live. Exits `1` when differences are pending.

## `scaffold`

Generate a starter C# model project from any source.

```
modelmeister scaffold --json  <export.json>                       --out ./Model
modelmeister scaffold --excel <model.xlsx>                        --out ./Model
modelmeister scaffold --url   <U> [auth]                          --out ./Model
                      [--namespace Generated.Model]
                      [--detect-base-classes] [--no-cvl-values]
```

The scaffolder bundles the `ModelMeister.Model.dll` under `lib/` of the output, so the generated
project builds standalone. Unsupported `=…` expressions are emitted as `Ex.Raw<T>(…)` calls and
flagged as warnings.

## `excel`

```
modelmeister excel export --json  <export.json> --out <model.xlsx>
modelmeister excel export --model <csproj|dll|dir> --out <model.xlsx>
modelmeister excel export --url   <U> [auth] --out <model.xlsx>
modelmeister excel import --excel <model.xlsx> --out <export.json>
```

One sheet per concept (EntityTypes, FieldTypes, FieldSets, Categories, CVLs, CvlValues, LinkTypes,
Roles, RestrictedPermissions, Completeness*, Languages). Sheets are real Excel Tables with
AutoFilter on every column header. Locale strings expand horizontally as `Name[en]`, `Name[sv]`, …
The shape mirrors the inriver export JSON exactly, so `Save → Load` is value-preserving and a
workbook handed to a subject-matter expert round-trips through `scaffold --excel`.

## `cvl`

Per-CVL value workflows (one sheet per CVL).

```
modelmeister cvl export --json <export.json> --out <cvls.xlsx>
modelmeister cvl export --url  <U> [auth]    --out <cvls.xlsx>
modelmeister cvl import --excel <cvls.xlsx>  --url <U> [auth] [--allow-deactivate] [--dry-run]
modelmeister cvl sync   --source-json <f>    --url <U> [auth] [--cvl <id>]
                                                              [--allow-deactivate] [--dry-run]
```

`cvl sync` requires a captured source snapshot (`modelmeister env snapshot --url <source> --out
source.json`) because the Remoting client is a process-wide singleton — two simultaneous
connections aren't possible from one process.

## `json`

```
modelmeister json merge --base <a.json> --overlay <b.json> --out <merged.json>
                        [--on-conflict overlay-wins|base-wins|fail]
```

Up to 50 conflicts are printed. If `--on-conflict fail` and conflicts exist, exits `4`; otherwise
the merged document is always written.

## `users`

```
modelmeister users list      --url <U> [auth]
modelmeister users template  --url <U> [auth] --out <users.xlsx>
modelmeister users provision --url <U> [auth] --excel <users.xlsx>
                             [--rest-base-url <U> --rest-api-key <K>]
                             [--dry-run]
```

The workbook is keyed on **Email**, which doubles as the inriver username — there is no separate
Username column.

Listing and role assignment work over Remoting. **User creation** requires a REST API key with
the `APIManageUsers` permission — pass `--rest-base-url` + `--rest-api-key`. Without REST
credentials, `provision` updates roles on users that already exist and skips creates. (A `403`
from the create call means the key lacks `APIManageUsers`.)

## `extensions`

Manage inriver extensions (Connectors).

```
modelmeister extensions list  --url <U> [auth] [--rest-base-url <U> --rest-api-key <K>]
modelmeister extensions start --url <U> [auth] --id <ext-id>
modelmeister extensions stop  --url <U> [auth] --id <ext-id>
modelmeister extensions logs  --url <U> [auth] --id <ext-id> [--count 50]
modelmeister extensions set   --url <U> [auth] --id <ext-id> --key <K> --value <V>
```

`start`/`stop` use the REST `/extensions/{id}:start` endpoint when REST credentials are supplied;
otherwise they fall back to the Remoting `SetConnectorStarted` call.

## `workflows`

Prints the cheat-sheet shown in the root `--help` preamble in full. No options.

## Interactive mode

`modelmeister` with no arguments launches a Spectre.Console menu (`InteractiveSession`) that wraps
the common commands via the same auth flow. Useful for ad-hoc work; not intended for CI. The
`modelmeister interactive` command does the same thing and is hidden from `--help`.
