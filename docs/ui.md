# UI guide

`ModelMeister.Ui` is an Avalonia 11 desktop app (`net9.0-windows`, `WinExe`) that wraps
the same library facade as the CLI behind a workflow-shaped front end. It exists for the
common case where you want a guided experience for connect → compare → apply, with a credentials
vault, a receipts history, and miscellaneous tools alongside.

## Window layout

```
┌───────────────────────────────────────────────────────────────────────────┐
│  ModelMeister  ●  Connected ▾  Env: PROD-EU  https://…   ☀/☾   Logs ▾    │  header
├──────────────┬────────────────────────────────────────────────────────────┤
│ Environments │  PUSH TO ENVIRONMENT                                       │
│ Model        │  Push the diff to the connected environment. …            │  page body
│ Policy       │                                                            │  (driven by
│ Compare      │  ┌─ Apply ───────────────────────────────────────────────┐ │   CurrentPage)
│ Apply        │  │  Dry-run · Apply · 24/24 succeeded · …                 │ │
│ History      │  └────────────────────────────────────────────────────────┘ │
│ Tools        │                                                            │
├──────────────┴────────────────────────────────────────────────────────────┤
│  Log drawer (collapsible)                                                  │
└───────────────────────────────────────────────────────────────────────────┘
```

The left rail (`MainWindowViewModel.Sections`) and the page area are bound through Avalonia's
view-locator convention: each `ViewModelBase` resolves to its corresponding `.axaml` view.

Header chrome:

- **Connection chip** — green when `IConnectionLifecycle.State == Connected`. Shows the connected
  environment's name + URL or the last connect error.
- **Stage badge** — `Unspecified / Dev / Test / Prod`. Prod connection paints a red prod-guard
  border around the whole window so destructive actions look conspicuous.
- **Theme toggle** — Dark / Light variant, persisted to `AppSettings.PreferDarkTheme`.
- **Log toggle** — opens the bottom drawer. An unseen-count badge appears when entries pile up
  while collapsed.

The workflow steps next to each rail item (`current` / `done` / `pending`) recompute on
connection / model / change-set changes (`MainWindowViewModel.RecomputeSteps`).

## Pages

### Environments

The credentials vault. Each row is an `EnvironmentEntry` (Name, URL, Stage, Notes, default-flag)
plus a per-row secret stored separately. Both files live under
`%APPDATA%/ModelMeister/` and are DPAPI-encrypted per Windows user.

| Action          | What it does                                                                       |
|-----------------|------------------------------------------------------------------------------------|
| Add / Edit      | Opens `EnvEditorDialog`. Captures name, Remoting URL + API key, optional REST base URL + REST API key, stage, notes. |
| Connect         | `IConnectionLifecycle.ConnectAsync(entry, secret)`. Double-clicking a row connects.|
| Disconnect      | Drops the live `InriverClient`.                                                    |
| Set default     | Persists `AppSettings.DefaultEnvId`. The app auto-connects to it on next launch (silent failure if the secret is missing). |
| Delete          | Removes the entry + its secret.                                                    |

The REST API base URL + key are **optional** and only needed for features Remoting can't do:
user creation (REST `POST /api/v1.0.0/system/users:provision` requires `APIManageUsers`), and the modern
Extensions endpoints. Leave them blank if you only need model diff/apply.

Vault paths:

```
%APPDATA%/ModelMeister/
  environments.dat   (DPAPI-encrypted JSON: list of EnvironmentEntry)
  secrets.dat        (DPAPI-encrypted JSON: id -> secret)
  settings.json      (plain JSON: AppSettings — theme, default env id, policy toggles, …)
```

A warning column flags entries whose secret is missing from the vault (e.g. after a manual
`environments.dat` restore).

### Model

Pick a `.csproj` or pre-built `.dll`, drag-and-drop it onto the page, or click a recents entry.
The page builds (`dotnet build`, out-of-process) and loads the assembly through
`ModelAssemblyLoader`, then runs `ModelValidator`. Errors and warnings are listed with their
source-file + line, click-through opens the file via `IFileOpener`.

Recents are tracked in `AppSettings.RecentModelPaths` and dedup'd by absolute path.

### Policy

Five toggles, each persisted to `AppSettings` and echoed through `PolicyViewModel.CurrentPolicy`
to the differ/applier:

| Toggle                            | Effect                                                                       |
|-----------------------------------|------------------------------------------------------------------------------|
| `OverwriteNamesAndDescriptions`   | Diff includes Name / Description differences instead of leaving them alone.  |
| `OverwriteCvlValues`              | Diff includes CVL value label changes.                                       |
| `AllowDeletes`                    | Diff emits Delete operations.                                                |
| `AllowDatatypeChange`             | Diff emits datatype-migration operations.                                    |
| `AllowCvlValueRename`             | Apply may rename a CVL key in place (migrates references).                   |

Defaults are conservative (every toggle off). Toggling notifies the parent
(`NotifyPolicyChanged`), which kicks the diff page to recompute its summary.

The `Summary` line at the top of the Compare page reads off this VM, so users always see which
destructive switches are armed.

### Compare

Runs a diff (`Shell.ComputeDiff`) of the loaded model against either:

- a freshly captured snapshot from the connected environment, or
- an offline JSON saved earlier.

Output is a three-level tree: **Concept → Operation → Row**. Counts of Adds, Updates, Deletes
and Warnings are shown at the top. Selecting a row reveals per-property deltas in a side pane.

The user can **skip** individual rows; skipped rows are subtracted from `EffectiveChanges()` and
the Apply page sees the reduced set. Skips persist until the diff is recomputed.

Text or JSON renderings of the current diff are available via Copy and Export.

### Apply

Pulls `EffectiveChanges()` from Compare and offers two actions:

| Action     | Effect                                                                                                                                 |
|------------|----------------------------------------------------------------------------------------------------------------------------------------|
| Dry-run    | Runs the applier with `dryRun: true`. No writes, no backup. Receipt is captured for review.                                            |
| Apply      | Opens `ConfirmApplyDialog` (counts + the policy echo). On accept, captures a backup snapshot, applies, writes a receipt under `.modelmeister/`. |

A progress bar tracks Completed / Succeeded / Failed in real time (the applier reports each
entry via `IProgress<ChangeReceiptEntry>`). Entries can be filtered All / Failed / Succeeded.

After a real apply, the parent view-model marks the workflow's Apply step `done`.

### History

Shows receipts and backups stored under `.modelmeister/` next to the loaded model:

```
<modelDir>/.modelmeister/
  receipts/<safe-url>/<timestamp>.json
  backups/<safe-url>/<timestamp>.model.json
```

Filters: text search, hide dry-runs. The Restore action takes a backup, runs it through the
scaffolder into a temp directory, loads the result as a `LoadedModel`, and routes to Compare so
the user can see the reverse change set before applying. Entries older than 30 days surface a
prune button.

### Tools

Independent utilities for tasks that don't fit the linear workflow. Each tab is a single focused
job — pick the one that matches what you want to do:

| Tool                   | Wraps                                              |
|------------------------|----------------------------------------------------|
| Scaffold from JSON     | `Shell.ScaffoldAsync(jsonPath, outDir, ns, detectBaseClasses, emitCvlValues)` |
| Scaffold from env      | `Shell.ScaffoldFromEnvAsync(outDir, ns, …)`        |
| Scaffold from Excel    | `Shell.ScaffoldFromExcelAsync(xlsxPath, outDir, ns, …, !skipCvlValues)` |
| Merge two JSON exports | `Shell.MergeJsonAsync(a, b, policy)`               |
| Snapshot env to JSON   | `Shell.SaveSnapshotAsync(snapshot, path)`          |
| Export to Excel        | `Shell.SaveSnapshotAsExcelAsync` / `SaveJsonAsExcelAsync` / `SaveLoadedModelAsExcelAsync` — pick env, JSON file, or C# model project as source; opens the xlsx after writing. |
| Probe connection       | `Shell.CaptureSnapshotAsync` followed by counts.   |

### Compare envs

Compare a saved snapshot (left) against the connected environment (right). Use `Tools → Export
snapshot` to capture a left-side snapshot of another env first. The report is read-only — push
differences via the normal `Compare` → `Apply` flow.

### CVL workbench

List, export and sync controlled value lists. Refresh loads the connected env's CVLs. Export
writes a per-CVL workbook for hand editing. Sync writes a saved snapshot's CVL values into the
connected env (with `Dry run` and `Allow deactivate` flags).

### Users

List the connected env's users + roles. Export Users workbook to seed an `.xlsx` with the current
users and the env's available roles. Provision applies an edited workbook back: user creation
needs a REST API key on the environment (set under **Edit env → REST API**); role assignment uses
Remoting.

### Extensions

List, start, stop, configure inriver extensions (Connectors). REST endpoints are preferred when a
REST key is configured; otherwise Remoting (`UtilityService.SetConnectorStarted`) does the work.
The events panel shows the last ~100 events for the selected extension.

## Settings & persistence

Everything user-visible the UI remembers lives in `AppSettings` (see `Models/AppSettings.cs`).
The store is plain JSON (`settings.json`), atomic-written through a `.tmp` rename. The vault is
two DPAPI-encrypted JSON files in the same directory.

Notable settings:

- `PreferDarkTheme` — bound to the header sun/moon toggle.
- `LogDrawerExpanded` — drawer state.
- `DefaultEnvId` — the env auto-connected at startup.
- `RecentModelPaths` — Model page recents.
- Policy toggles (mirrored to `PolicyViewModel`).

## Logging & toasts

`IAppLog` is a process-wide log + toast bus shared by every VM (`AppLog`). Entries (Info / Warn /
Error / Success) feed both the bottom drawer and the toast surface. The drawer keeps an unseen
counter while collapsed.

## Reusing the engine

Every page goes through the `Shell` facade (`Services/Shell.cs`). The facade is a thin async
wrapper over the same libraries the CLI uses:

- `LoadModelAsync` → `ModelAssemblyLoader.LoadFromPath`
- `Validate` → `ModelValidator.Validate`
- `CaptureSnapshotAsync` → `InriverSnapshot.Capture`
- `ComputeDiff` → `ModelDiffer.Diff`
- `ApplyAsync` → `ChangeApplier.ApplyAsync`
- `ScaffoldAsync` / `ScaffoldFromEnvAsync` / `ScaffoldFromLiveModelAsync` → `ProjectScaffolder.Scaffold`
- `MergeJsonAsync` → `ModelMerger.Merge`

That means the UI is a thin presentation layer — the modelling semantics, validation codes, and
exit-coded apply contract documented elsewhere apply identically to interactions started from the
app.
