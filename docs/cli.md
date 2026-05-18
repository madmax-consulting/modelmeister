# CLI reference

`modelmeister` is the single executable produced by `ModelMeister.Cli`. Running it with
no arguments drops into an interactive Spectre.Console menu; otherwise it is a standard
`System.CommandLine` host with one subcommand per top-level action.

```
modelmeister <command> [options]
modelmeister                       # interactive menu
modelmeister --no-color <command>  # disable ANSI colours (also: NO_COLOR env var)
```

## Exit codes

These are stable contract — CI workflows under `.github/workflows/` consume them directly,
so don't change values without coordinating with the workflows.

| Code | Constant            | Meaning                                                                     |
|-----:|---------------------|-----------------------------------------------------------------------------|
| 0    | `Success`           | Operation succeeded; no further action required.                            |
| 1    | `ChangesPending`    | `diff --fail-on-changes` found changes, or `apply` was aborted / dry-run.   |
| 2    | `UsageError`        | Missing, conflicting, or invalid arguments.                                 |
| 3    | `ValidationFailed`  | `validate` produced at least one error issue.                               |
| 4    | `OperationFailed`   | Connection error, IO error, or partial apply.                               |

See `src/ModelMeister.Cli/ExitCodes.cs` for the canonical definition.

## Connection options

Every command that talks to inriver accepts the same connection options (declared once in
`ConnectionOptions.cs`):

| Option         | Purpose                                                      |
|----------------|--------------------------------------------------------------|
| `--url`        | inriver Remoting URL. **Required** for connected commands.   |
| `--api-key`    | API key. Falls back to the `INRIVER_API_KEY` env var.        |
| `--username`   | Username for legacy credential-based auth.                   |
| `--password`   | Password.                                                    |
| `--environment`| Optional inriver environment name (used with username/password). |

Either an API key (explicit or via `INRIVER_API_KEY`) **or** a username/password pair is required.
If neither is supplied, the command returns `2 UsageError`.

---

## `scaffold`

Generate a starter C# model project from either a JSON export or a live environment snapshot.

```
modelmeister scaffold --json export.json [--out ./GeneratedModel] [--namespace Generated.Model]
                      [--detect-base-classes] [--no-cvl-values]

modelmeister scaffold --url https://… --api-key …
                      [--out ./GeneratedModel] [--namespace Generated.Model]
                      [--detect-base-classes] [--no-cvl-values]
```

Exactly one of `--json` or `--url` must be supplied — passing both, or neither, returns
`2 UsageError`.

| Option                     | Default            | Notes                                                                 |
|----------------------------|--------------------|-----------------------------------------------------------------------|
| `--json`                   | —                  | Path to an inriver export JSON.                                       |
| `--url` (+ auth options)   | —                  | Fetch the live model via the Remoting API.                            |
| `--out`                    | `./GeneratedModel` | Output directory.                                                     |
| `--namespace`              | `Generated.Model`  | Root namespace for the generated project.                             |
| `--detect-base-classes`    | `true`             | Extract shared field sets into abstract base classes.                 |
| `--no-cvl-values`          | `false`            | Skip emitting CVL values — the generated `Cvl` classes remain empty.  |

The scaffolder bundles the `ModelMeister.Model.dll` under `lib/` of the output, so the
generated project builds standalone. Unsupported `=…` expressions are emitted as `Ex.Raw<T>(…)`
calls with a `warn:` message; the first 20 warnings are printed, with an "and N more" tail.

## `validate`

Statically validate a code-defined model. Catches every `MMxxx` issue listed in
[`validation-codes.md`](validation-codes.md).

```
modelmeister validate --model path/to/MyModel.csproj [--json]
```

| Option         | Notes                                                                                  |
|----------------|----------------------------------------------------------------------------------------|
| `--model`      | Path to a `.csproj` or pre-built `.dll`. **Required.**                                 |
| `--json`       | Emit a JSON report instead of the Spectre rendering. Suitable for CI step summaries.   |

Behaviour: exits `0` when the model has no errors (warnings are allowed), `3` when at least one
error is present, `4` on load/IO failures other than `CvlSourceMissingException` (which is
surfaced as a synthetic MM076 issue).

The JSON shape:

```json
{
  "Valid": true,
  "ErrorCount": 0,
  "WarningCount": 1,
  "EntityTypes": 12, "Cvls": 7, "LinkTypes": 5,
  "Issues": [ { "Severity": "Warning", "Code": "MM060", "Message": "…" } ]
}
```

## `describe`

Print a summary table — counts and a preview of identifiers per concept.

```
modelmeister describe --model path/to/MyModel.csproj [--json]
```

The table reports entity types, CVLs, link types, categories, fieldsets, roles and languages.
`--json` produces a tooling-friendly payload with full id lists per concept.

## `status`

Ping an environment and print concept counts. Useful as a connectivity smoke test.

```
modelmeister status --url $INRIVER_URL --api-key $INRIVER_API_KEY
```

Returns `0` on success, `4` if the connection or snapshot fails. Counts cover entity types, CVLs,
CVL values (summed), link types, categories, fieldsets, roles, permissions and languages.

## `diff`

Show what `apply` would change against an environment, with no writes.

```
modelmeister diff --model MyModel.csproj --url $INRIVER_URL --api-key …
                  [--format tree|text|json]
                  [--out path/to/file]
                  [--fail-on-changes]
                  [--allow-deletes] [--allow-datatype-change]
```

| Option                      | Default | Notes                                                                  |
|-----------------------------|---------|------------------------------------------------------------------------|
| `--format`                  | `tree`  | `tree` is the rich Spectre rendering; `text` is plain text; `json` is structured. |
| `--out`                     | —       | Also write the rendering to the given path (text/json based on `--format`). |
| `--fail-on-changes`         | `false` | Exit `1` if any changes are detected (CI gate).                        |
| `--allow-deletes`           | `false` | Permit the differ to emit Delete operations.                           |
| `--allow-datatype-change`   | `false` | Permit field datatype changes.                                         |

`--fail-on-changes` is the signal `model-diff.yml` uses to gate merges. Without it, `diff`
returns `0` regardless of whether changes were found.

## `apply`

Apply a code-defined model to inriver. Captures a backup snapshot before any mutations and writes
a receipt afterwards.

```
modelmeister apply --model MyModel.csproj --url $INRIVER_URL --api-key …
                   [--dry-run] [--yes]
                   [--allow-deletes] [--allow-datatype-change] [--allow-cvl-value-rename]
```

| Option                       | Default | Notes                                                                                  |
|------------------------------|---------|----------------------------------------------------------------------------------------|
| `--dry-run`                  | `false` | Compute and print the diff, take a backup, but skip writes. Exits `1` if changes exist. |
| `--yes`                      | `false` | Skip the interactive confirmation prompt.                                              |
| `--allow-deletes`            | `false` | Permit Delete operations.                                                              |
| `--allow-datatype-change`    | `false` | Permit datatype changes.                                                               |
| `--allow-cvl-value-rename`   | `false` | Permit renaming a CVL value key (migrates references).                                 |

Apply flow:

1. Connect, snapshot the env for the backup (`.modelmeister/backups/<env>/<timestamp>.model.json`).
2. Diff. If empty, exit `0`. If `--dry-run`, exit `1`.
3. If interactive (no `--yes`), prompt `Apply N change(s) to <url>?`. Decline → exit `1`.
4. Apply, writing a receipt to `.modelmeister/receipts/<env>/<timestamp>.json`.
5. Exit `0` on full success, `4` if any change failed (`receipt.Failed > 0`).

The receipt and backup paths are echoed to the console. The directory layout mirrors that used by
the UI's History page, so receipts produced by the CLI surface there automatically.

## `export`

Snapshot a live model to JSON. The output is in the same shape `scaffold --json` consumes and
`merge` operates on.

```
modelmeister export --url $INRIVER_URL --api-key … --out live.json
```

`--out` is required. Returns `0` on success, `4` on transport failure.

## `merge`

Combine two JSON exports into one. Lower-priority side first, higher-priority overlay second.

```
modelmeister merge --base a.json --overlay b.json --out merged.json
                   [--on-conflict overlay-wins|base-wins|fail]
```

| Option            | Default        | Notes                                                                       |
|-------------------|----------------|-----------------------------------------------------------------------------|
| `--base`          | —              | Lower-priority JSON. **Required.**                                          |
| `--overlay`       | —              | Higher-priority JSON. **Required.**                                         |
| `--out`           | —              | Output path. **Required.**                                                  |
| `--on-conflict`   | `overlay-wins` | Conflict resolution policy.                                                 |

Up to 50 conflicts are printed (`yellow ·`); the count overflow is summarised. If the policy is
`fail` and conflicts exist, exits `4`. Otherwise the merged document is always written.

## `interactive`

Launches the menu manually. Identical to running `modelmeister` with no arguments. The menu is
defined in `Interactive/InteractiveSession.cs` and wraps `scaffold`, `merge`, `validate`,
`describe`, `status`, `diff`, `apply`, `export` and "scaffold from live" via the same auth flow as
the non-interactive commands.

## Usage examples

CI gate (PR pipeline):

```bash
modelmeister validate --model MyModel.csproj --json > validation.json   # exit 3 on errors
modelmeister diff --model MyModel.csproj --url $INRIVER_URL --fail-on-changes
```

Production apply (after manual approval in `workflow_dispatch`):

```bash
modelmeister apply --model MyModel.csproj --url $INRIVER_URL --dry-run
modelmeister apply --model MyModel.csproj --url $INRIVER_URL --yes
modelmeister diff  --model MyModel.csproj --url $INRIVER_URL --fail-on-changes  # post-apply verification
```

Bootstrap a project from a live environment:

```bash
modelmeister scaffold --url $INRIVER_URL --out ./PimModel --namespace Acme.Pim --detect-base-classes
```

## `excel`

Excel workbook export/import for the full model.

```
modelmeister excel export --json <export.json> --out <model.xlsx>
modelmeister excel export --url <url> [auth] --out <model.xlsx>
modelmeister excel import --xlsx <model.xlsx> --out <export.json>
```

The workbook has one sheet per concept (`EntityTypes`, `FieldTypes`, `FieldSets`, `Categories`,
`CVLs`, `CvlValues`, `LinkTypes`, `Roles`, `RestrictedPermissions`, `Completeness*`, `Languages`).
Locale strings expand horizontally as `Name[en]`, `Name[sv]`, … columns. The shape mirrors the
inriver export JSON exactly, so a `Save → Load` cycle is value-preserving and a workbook can be
handed off to subject-matter experts for offline editing.

To scaffold from a workbook: `modelmeister scaffold --excel <model.xlsx> ...` — same options as
`--json`.

## `cvl`

CVL value workflows. Use a per-CVL workbook (one sheet per CVL) for hand editing, or sync values
between environments via a saved snapshot.

```
modelmeister cvl export --json <export.json> --out <cvls.xlsx>
modelmeister cvl export --url <url> [auth] --out <cvls.xlsx>
modelmeister cvl import --xlsx <cvls.xlsx> --url <target> [auth] [--allow-deactivate] [--dry-run]
modelmeister cvl sync --source-json <source.json> --url <target> [auth] [--cvl <id>] [--allow-deactivate] [--dry-run]
```

`cvl sync` requires a captured source snapshot (`modelmeister export --url <source> --out source.json`)
because the Remoting client is a process-wide singleton — you cannot connect to two environments
in the same process.

## `compare-envs`

Compare two environments. The left side can be a saved JSON snapshot OR a live URL; the right
side is always live.

```
modelmeister compare-envs --left-json snap.json --right-url <url> [--right-api-key …]
modelmeister compare-envs --left-url <a> --left-api-key … --right-url <b> --right-api-key …
                          [--format text|json] [--out report.txt]
```

Exits `0` when identical, `1` when differences are pending. The report is read-only — push
differences via the regular `diff` / `apply` pipeline (scaffold one side, apply against the other).

## `users`

```
modelmeister users list --url <url> [auth]
modelmeister users export-template --url <url> [auth] --out users.xlsx
modelmeister users provision --url <url> [auth] --excel users.xlsx
                             [--rest-base-url https://apieuw.productmarketingcloud.com --rest-api-key …]
                             [--dry-run]
```

Listing and role assignment work over Remoting. **User creation** requires a REST API key with
`APIManageUsers` permission — pass `--rest-base-url` + `--rest-api-key`, or run against existing
users (Remoting can update role assignments either way).

## `extensions`

Manage inriver extensions (Connectors).

```
modelmeister extensions list  --url <url> [auth] [--rest-base-url … --rest-api-key …]
modelmeister extensions start --url <url> [auth] --id <extension-id>
modelmeister extensions stop  --url <url> [auth] --id <extension-id>
modelmeister extensions logs  --url <url> [auth] --id <extension-id> [--count 50]
modelmeister extensions set   --url <url> [auth] --id <extension-id> --key K --value V
```

start/stop will use the REST `/extensions/{id}:start` endpoint when REST credentials are supplied
and fall back to Remoting (`SetConnectorStarted`) otherwise.
