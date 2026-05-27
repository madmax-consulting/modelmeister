using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelMeister.Excel;
using ModelMeister.Inriver.Apply;
using ModelMeister.Inriver.Backup;
using ModelMeister.Inriver.Cvl;
using ModelMeister.Inriver.Diff;
using ModelMeister.Inriver;
using ModelMeister.Inriver.Extensions;
using ModelMeister.Inriver.ServerSettings;
using ModelMeister.Inriver.HtmlTemplates;
using ModelMeister.Inriver.Snapshot;
using ModelMeister.Inriver.Users;
using ModelMeister.Inriver.WorkAreas;
using ModelMeister.Loading;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Validation;
using ModelMeister.Rest;
using ModelMeister.Scaffolder;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Facade over the toolkit libraries. Wraps blocking calls (csproj build, remoting reads) in
/// <see cref="Task.Run(System.Action)"/> so view-models stay off the UI thread.
/// </summary>
public sealed class Shell
{
    private readonly IConnectionLifecycle _connection;
    private readonly ModelAssemblyLoader _loader = new();

    public Shell(IConnectionLifecycle connection)
    {
        _connection = connection;
    }

    /// <summary>Build (if necessary) and load a model from a csproj or pre-built DLL on a worker thread.</summary>
    public Task<LoadedModel> LoadModelAsync(string csprojOrDll, CancellationToken ct = default)
        => Task.Run(() => _loader.LoadFromPath(csprojOrDll), ct);

    /// <summary>Run the validator over the loaded model. Cheap — runs synchronously.</summary>
    public ValidationResult Validate(LoadedModel model) => ModelValidator.Validate(model);

    /// <summary>Capture a <see cref="LiveModel"/> snapshot from the currently connected inriver env.</summary>
    public Task<LiveModel> CaptureSnapshotAsync(CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new InriverSnapshot(client).Capture(), ct);
    }

    /// <summary>Serialise <paramref name="snapshot"/> to <paramref name="path"/>, creating directories as needed.</summary>
    public Task SaveSnapshotAsync(LiveModel snapshot, string path, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, LiveModelJson.Serialize(snapshot));
        }, ct);

    /// <summary>Pure function: compute the change set required to make <paramref name="live"/> match <paramref name="code"/>.</summary>
    public ModelChangeSet ComputeDiff(LoadedModel code, LiveModel live, MergePolicy policy)
        => ModelDiffer.Diff(code, live, policy);

    /// <summary>Apply (or dry-run) a change set against the currently connected env.</summary>
    public Task<ChangeReceipt> ApplyAsync(
        ModelChangeSet changes,
        LoadedModel code,
        LiveModel live,
        bool dryRun,
        string? backupPath,
        IProgress<ChangeReceiptEntry>? progress,
        CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        var applier = new ChangeApplier(client);
        return applier.ApplyAsync(changes, code, live, dryRun, backupPath, progress, ct);
    }

    /// <summary>Scaffold a typed C# model project from a JSON export.</summary>
    public Task<ScaffoldResult> ScaffoldAsync(string jsonPath, string outDir, string rootNamespace, bool detectBaseClasses, bool emitCvlValues, CancellationToken ct = default)
        => Task.Run(() => new ProjectScaffolder().Scaffold(jsonPath, outDir, rootNamespace, detectBaseClasses, emitCvlValues), ct);

    /// <summary>Scaffold a typed C# model project directly from a live snapshot of the connected env.</summary>
    public Task<ScaffoldResult> ScaffoldFromEnvAsync(string outDir, string rootNamespace, bool detectBaseClasses, bool emitCvlValues, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() =>
        {
            var snapshot = new InriverSnapshot(client).Capture();
            var model = LiveModelConverter.ToJsonModel(snapshot);
            return new ProjectScaffolder().Scaffold(model, outDir, rootNamespace, detectBaseClasses, emitCvlValues);
        }, ct);
    }

    /// <summary>
    /// Scaffold a typed C# model project from an in-memory LiveModel. Used by the restore-from-backup
    /// flow: the backup is loaded as LiveModel, scaffolded to a temp directory, then re-loaded as a
    /// LoadedModel so the normal compare/apply pipeline can roll the live env back to that state.
    /// </summary>
    public Task<ScaffoldResult> ScaffoldFromLiveModelAsync(LiveModel live, string outDir, string rootNamespace, bool detectBaseClasses, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var model = LiveModelConverter.ToJsonModel(live);
            return new ProjectScaffolder().Scaffold(model, outDir, rootNamespace, detectBaseClasses);
        }, ct);

    /// <summary>
    /// Scaffold an in-memory <see cref="LiveModel"/> to a temp C# project and load it as a
    /// <see cref="LoadedModel"/> — the same round-trip restore-from-backup uses. Lets env→env
    /// promotion reuse the regular diff/apply pipeline by treating the source env as the "code" side.
    /// Multi-second (runs <c>dotnet build</c>); callers cache the result per direction.
    /// </summary>
    public async Task<LoadedModel> LoadModelFromLiveAsync(LiveModel live, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var tempDir = Path.Combine(Path.GetTempPath(), $"modelmeister-promote-{token}");
        Directory.CreateDirectory(tempDir);
        await ScaffoldFromLiveModelAsync(live, tempDir, "Promote.PimModel", detectBaseClasses: true, ct).ConfigureAwait(false);
        var csproj = Directory.EnumerateFiles(tempDir, "*.csproj").FirstOrDefault()
            ?? throw new InvalidOperationException("Scaffold did not produce a csproj.");
        return await LoadModelAsync(csproj, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Promote the given concept scopes from <paramref name="sourceLoaded"/> (a model loaded from the
    /// source env) into the currently-connected <b>target</b> env. Re-captures the target fresh (the
    /// applier needs live ids), diffs, filters to the scopes via <see cref="ModelChangeFilter"/>, then
    /// applies through the regular <c>ChangeApplier</c> (which re-sorts by ApplyOrder).
    /// </summary>
    public async Task<ChangeReceipt> PromoteConceptsAsync(
        LoadedModel sourceLoaded,
        IEnumerable<PromoteScope> scopes,
        MergePolicy policy,
        string? backupPath,
        IProgress<ChangeReceiptEntry>? progress = null,
        CancellationToken ct = default)
    {
        var targetLive = await CaptureSnapshotAsync(ct).ConfigureAwait(false);

        // Back up the target before mutating it (ChangeApplier only records the path, it doesn't write).
        if (!string.IsNullOrEmpty(backupPath))
            await SaveSnapshotAsync(targetLive, backupPath!, ct).ConfigureAwait(false);

        var full = ComputeDiff(sourceLoaded, targetLive, policy);
        var filtered = ModelChangeFilter.ForConcepts(full, scopes);
        return await ApplyAsync(filtered, sourceLoaded, targetLive, dryRun: false, backupPath, progress, ct).ConfigureAwait(false);
    }

    /// <summary>Merge two JSON model exports under the given conflict policy and return the merged document plus any conflicts.</summary>
    public Task<(InriverModelJson Merged, List<string> Conflicts)> MergeJsonAsync(
        string basePath, string overlayPath, MergeConflictPolicy policy, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var b = InriverModelJson.Load(basePath);
            var o = InriverModelJson.Load(overlayPath);
            return new ModelMerger(policy).Merge(b, o);
        }, ct);

    /// <summary>Deserialise a saved snapshot JSON back into a <see cref="LiveModel"/>.</summary>
    public Task<LiveModel> LoadSnapshotJsonAsync(string path, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var text = File.ReadAllText(path);
            return LiveModelJson.Deserialize(text) ?? throw new InvalidDataException("Snapshot JSON did not deserialise.");
        }, ct);

    /// <summary>Directory under which apply receipts for <paramref name="envUrl"/> are stored.</summary>
    public string GetReceiptsDir(string envUrl, string modelDir)
        => Path.Combine(modelDir, ".modelmeister", "receipts", Paths.SafeUrlSegment(envUrl));

    /// <summary>Directory under which apply backups for <paramref name="envUrl"/> are stored.</summary>
    public string GetBackupsDir(string envUrl, string modelDir)
        => Path.Combine(modelDir, ".modelmeister", "backups", Paths.SafeUrlSegment(envUrl));

    // ---------------- Excel I/O ----------------

    /// <summary>Save a captured snapshot to an Excel workbook for human editing.</summary>
    public Task SaveSnapshotAsExcelAsync(LiveModel snapshot, string xlsxPath, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var json = LiveModelConverter.ToJsonModel(snapshot);
            ModelWorkbook.Save(json, xlsxPath);
        }, ct);

    /// <summary>Save a JSON model export to an Excel workbook.</summary>
    public Task SaveJsonAsExcelAsync(string jsonPath, string xlsxPath, CancellationToken ct = default)
        => Task.Run(() => ModelWorkbook.Save(InriverModelJson.Load(jsonPath), xlsxPath), ct);

    /// <summary>Load a C# model project (csproj/dll/dir) and save it to an Excel workbook.</summary>
    public Task SaveLoadedModelAsExcelAsync(string csprojOrDll, string xlsxPath, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var loaded = _loader.LoadFromPath(csprojOrDll);
            var json = LoadedModelConverter.ToJsonModel(loaded);
            ModelWorkbook.Save(json, xlsxPath);
        }, ct);

    /// <summary>Save a CVL-values workbook from a captured snapshot.</summary>
    public Task SaveCvlValuesAsExcelAsync(LiveModel snapshot, string xlsxPath, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var json = LiveModelConverter.ToJsonModel(snapshot);
            CvlValuesWorkbook.Save(json, xlsxPath);
        }, ct);

    /// <summary>Load a CVL-values workbook and project it into a minimal source <see cref="LiveModel"/>
    /// (CVLs + values only). Shared with the CLI's <c>cvl import</c> path via
    /// <see cref="LiveModelConverter.CvlSourceFromJson"/>, so the UI import behaves identically.</summary>
    public Task<LiveModel> LoadCvlImportSourceAsync(string xlsxPath, CancellationToken ct = default)
        => Task.Run(() => LiveModelConverter.CvlSourceFromJson(CvlValuesWorkbook.Load(xlsxPath)), ct);

    /// <summary>Write a one-CVL example workbook the user can edit and re-import.</summary>
    public Task SaveCvlTemplateAsync(string xlsxPath, CancellationToken ct = default)
        => Task.Run(() => CvlValuesWorkbook.SaveTemplate(xlsxPath), ct);

    /// <summary>Scaffold from an Excel workbook.</summary>
    public Task<ScaffoldResult> ScaffoldFromExcelAsync(string xlsxPath, string outDir, string rootNamespace, bool detectBaseClasses, bool emitCvlValues, CancellationToken ct = default)
        => Task.Run(() => ExcelScaffolder.ScaffoldFromExcel(xlsxPath, outDir, rootNamespace, detectBaseClasses, emitCvlValues), ct);

    // ---------------- Environment compare ----------------

    /// <summary>Compare the currently-connected env to a saved JSON snapshot (left side).</summary>
    public Task<EnvironmentDiff> CompareEnvToSnapshotAsync(LiveModel snapshot, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() =>
        {
            var right = new InriverSnapshot(client).Capture();
            return EnvironmentComparer.Compare(snapshot, right);
        }, ct);
    }

    /// <summary>Compare two saved JSON snapshots.</summary>
    public Task<EnvironmentDiff> CompareSnapshotsAsync(LiveModel left, LiveModel right, CancellationToken ct = default)
        => Task.Run(() => EnvironmentComparer.Compare(left, right), ct);

    // ---------------- CVL sync ----------------

    /// <summary>
    /// Sync CVL values from a snapshot into the currently-connected env. The snapshot is the source
    /// of truth; the live env receives the writes.
    /// </summary>
    public Task<List<CvlSync.Result>> SyncCvlsAsync(
        LiveModel source, IReadOnlyList<string> cvlIds, bool allowDeactivate, bool dryRun, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(async () =>
        {
            var sync = new CvlSync(source, client);
            var opts = new CvlSync.Options(AllowDeactivate: allowDeactivate, OverwriteValues: true, DryRun: dryRun);
            var results = new List<CvlSync.Result>();
            foreach (var id in cvlIds)
            {
                var plan = sync.PlanFor(id, opts);
                if (plan.Total == 0) continue;
                results.Add(await sync.ApplyAsync(plan, opts, ct).ConfigureAwait(false));
            }
            return results;
        }, ct);
    }

    /// <summary>Surgical upsert of a single CVL value into the currently-connected env.</summary>
    /// <remarks>Caller must ensure the parent CVL exists on the connected env first.</remarks>
    public Task ApplyCvlValueAsync(string cvlId, LiveCvlValue source, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => CvlSync.ApplyValueAsync(client, cvlId, source, ct), ct);
    }

    /// <summary>Surgical delete of a single CVL value from the currently-connected env.</summary>
    public Task DeleteCvlValueAsync(string cvlId, string key, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => CvlSync.DeleteValueAsync(client, cvlId, key, ct), ct);
    }

    /// <summary>Pre-check used by per-value promote: returns true when the parent CVL exists on the connected env.</summary>
    public Task<bool> CvlExistsAsync(string cvlId, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => CvlSync.TargetHasCvl(client, cvlId), ct);
    }

    // ---------------- CVL admin (workbench CRUD) ----------------

    /// <summary>Create a new CVL definition on the connected env.</summary>
    public Task AddCvlAsync(string id, ModelMeister.Model.Primitives.CvlDataType dataType, string? parentId, bool customValueList, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new CvlAdmin(client).AddCvlAsync(id, dataType, parentId, customValueList, ct), ct);
    }

    /// <summary>Update an existing CVL definition (datatype / parent / custom flag) on the connected env.</summary>
    public Task UpdateCvlAsync(string id, ModelMeister.Model.Primitives.CvlDataType dataType, string? parentId, bool customValueList, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new CvlAdmin(client).UpdateCvlAsync(id, dataType, parentId, customValueList, ct), ct);
    }

    /// <summary>Delete a CVL definition (and its values) from the connected env.</summary>
    public Task DeleteCvlAsync(string id, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new CvlAdmin(client).DeleteCvlAsync(id, ct), ct);
    }

    /// <summary>List a CVL's values (index order) for the in-place value editor.</summary>
    public Task<IReadOnlyList<LiveCvlValue>> ListCvlValuesAsync(string cvlId, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new CvlAdmin(client).ListValues(cvlId), ct);
    }

    /// <summary>Add or update a single CVL value (matched by key) on the connected env.</summary>
    public Task UpsertCvlValueAsync(string cvlId, LiveCvlValue value, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new CvlAdmin(client).UpsertValueAsync(cvlId, value, ct), ct);
    }

    // ---------------- Users ----------------

    public Task<IReadOnlyList<ModelMeister.Inriver.Users.UserSummary>> ListUsersAsync(CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new UserProvisioning(client).ListUsers(), ct);
    }

    public Task<IReadOnlyList<string>> ListRoleNamesAsync(CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new UserProvisioning(client).ListRoleNames(), ct);
    }

    public Task<UserProvisioning.ProvisionResult> ProvisionUserAsync(
        UserProvisioning.UserSpec spec, EnvironmentSecret? secret, EnvironmentEntry env, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        InriverRestClient? rest = null;
        if (!string.IsNullOrEmpty(env.RestBaseUrl) && !string.IsNullOrEmpty(secret?.RestApiKey))
            rest = new InriverRestClient(env.RestBaseUrl!, secret.RestApiKey!);
        var prov = new UserProvisioning(client, rest);
        return Task.Run(() => prov.ProvisionAsync(spec, ct), ct);
    }

    // ---------------- Roles ----------------

    public Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new RoleProvisioning(client).ListRoles(), ct);
    }

    public Task<IReadOnlyList<string>> ListPermissionNamesAsync(CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new RoleProvisioning(client).ListPermissionNames(), ct);
    }

    public Task<RoleProvisioning.ProvisionResult> ProvisionRoleAsync(
        RoleProvisioning.RoleSpec spec, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new RoleProvisioning(client).ProvisionAsync(spec, ct), ct);
    }

    /// <summary>Delete a role by name from the connected env (resolves the live id internally).</summary>
    public Task<RoleProvisioning.ProvisionResult> DeleteRoleAsync(string roleName, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new RoleProvisioning(client).DeleteAsync(roleName, ct), ct);
    }

    /// <summary>Bulk add or remove a single permission across many roles in the connected env.</summary>
    public Task<IReadOnlyList<RoleProvisioning.ProvisionResult>> BulkSetRolePermissionAsync(
        IReadOnlyList<string> roleNames, string permission, bool add, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new RoleProvisioning(client).SetPermissionOnRolesAsync(roleNames, permission, add, ct), ct);
    }

    // ---------------- Restricted fields ----------------

    public Task<IReadOnlyList<RestrictedFieldSummary>> ListRestrictedFieldsAsync(CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new RestrictedFieldProvisioning(client).ListRestrictedFields(), ct);
    }

    public Task<RestrictedFieldProvisioning.ProvisionResult> AddRestrictedFieldAsync(
        RestrictedFieldProvisioning.RestrictedFieldSpec spec, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new RestrictedFieldProvisioning(client).AddAsync(spec, ct), ct);
    }

    public Task<RestrictedFieldProvisioning.ProvisionResult> DeleteRestrictedFieldAsync(int liveId, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new RestrictedFieldProvisioning(client).DeleteAsync(liveId, ct), ct);
    }

    // ---------------- Extensions ----------------

    public Task<IReadOnlyList<ExtensionsService.ExtensionInfo>> ListExtensionsAsync(EnvironmentEntry? env, EnvironmentSecret? secret, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        InriverRestClient? rest = null;
        if (env is not null && secret is not null && !string.IsNullOrEmpty(env.RestBaseUrl) && !string.IsNullOrEmpty(secret.RestApiKey))
            rest = new InriverRestClient(env.RestBaseUrl!, secret.RestApiKey!);
        return Task.Run(() => new ExtensionsService(client, rest).List(), ct);
    }

    public Task<bool> StartExtensionAsync(string id, EnvironmentEntry? env, EnvironmentSecret? secret, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        InriverRestClient? rest = null;
        if (env is not null && secret is not null && !string.IsNullOrEmpty(env.RestBaseUrl) && !string.IsNullOrEmpty(secret.RestApiKey))
            rest = new InriverRestClient(env.RestBaseUrl!, secret.RestApiKey!);
        return Task.Run(() => new ExtensionsService(client, rest).StartAsync(id, ct), ct);
    }

    public Task<bool> StopExtensionAsync(string id, EnvironmentEntry? env, EnvironmentSecret? secret, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        InriverRestClient? rest = null;
        if (env is not null && secret is not null && !string.IsNullOrEmpty(env.RestBaseUrl) && !string.IsNullOrEmpty(secret.RestApiKey))
            rest = new InriverRestClient(env.RestBaseUrl!, secret.RestApiKey!);
        return Task.Run(() => new ExtensionsService(client, rest).StopAsync(id, ct), ct);
    }

    public Task<IReadOnlyList<ExtensionsService.ExtensionEvent>> ExtensionEventsAsync(string id, int max, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ExtensionsService(client).Events(id, max), ct);
    }

    /// <summary>Read the latest events across every extension (triage feed).</summary>
    public Task<IReadOnlyList<ExtensionsService.ExtensionEvent>> LatestExtensionEventsAsync(int max = 200, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ExtensionsService(client).LatestEvents(max), ct);
    }

    public Task<bool> RunExtensionAsync(string id, EnvironmentEntry? env, EnvironmentSecret? secret, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        InriverRestClient? rest = null;
        if (env is not null && secret is not null && !string.IsNullOrEmpty(env.RestBaseUrl) && !string.IsNullOrEmpty(secret.RestApiKey))
            rest = new InriverRestClient(env.RestBaseUrl!, secret.RestApiKey!);
        return Task.Run(() => new ExtensionsService(client, rest).RunAsync(id, ct), ct);
    }

    public Task<bool> SetExtensionSettingAsync(string id, string key, string value, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ExtensionsService(client).SetSettingAsync(id, key, value, ct), ct);
    }

    public Task<bool> DeleteExtensionSettingAsync(string id, string key, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ExtensionsService(client).DeleteSettingAsync(id, key, ct), ct);
    }

    public Task<bool> DeleteExtensionAsync(string id, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ExtensionsService(client).DeleteAsync(id, ct), ct);
    }

    public Task<IReadOnlyList<ExtensionsService.ExtensionStateRow>> ListExtensionStatesAsync(string? extensionId = null, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() =>
        {
            var svc = new ExtensionsService(client);
            return string.IsNullOrEmpty(extensionId) ? svc.ListAllStates() : svc.ListStates(extensionId);
        }, ct);
    }

    public Task<ExtensionsService.ExtensionStateRow?> AddExtensionStateAsync(string connectorId, string data, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ExtensionsService(client).AddStateAsync(connectorId, data, ct), ct);
    }

    public Task<bool> UpdateExtensionStateAsync(int id, string connectorId, string data, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ExtensionsService(client).UpdateStateAsync(id, connectorId, data, ct), ct);
    }

    public Task<bool> DeleteExtensionStateAsync(int id, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ExtensionsService(client).DeleteStateAsync(id, ct), ct);
    }

    public Task<bool> DeleteAllExtensionStatesAsync(CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ExtensionsService(client).DeleteAllStatesAsync(ct), ct);
    }

    // ---------------- Server settings ----------------

    public Task<IReadOnlyDictionary<string, string>> ListServerSettingsAsync(CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ServerSettingsService(client).GetAll(), ct);
    }

    public Task<bool> SetServerSettingAsync(string key, string value, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ServerSettingsService(client).SetAsync(key, value, ct), ct);
    }

    public Task<bool> DeleteServerSettingAsync(string key, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ServerSettingsService(client).DeleteAsync(key, ct), ct);
    }

    public Task<ServerSettingsService.BulkResult> BulkApplyServerSettingsAsync(
        IEnumerable<KeyValuePair<string, string?>> entries, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new ServerSettingsService(client).BulkApplyAsync(entries, ct), ct);
    }

    // ---------------- Scoped backup capture ----------------

    /// <summary>Capture a <see cref="UsersBackup"/> from the currently connected env.</summary>
    public Task<UsersBackup> CaptureUsersBackupAsync(BackupMetadata metadata, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => UsersBackup.Capture(new UserProvisioning(client), metadata), ct);
    }

    /// <summary>Capture a <see cref="ServerSettingsBackup"/> from the currently connected env.</summary>
    public Task<ServerSettingsBackup> CaptureServerSettingsBackupAsync(BackupMetadata metadata, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => ServerSettingsBackup.Capture(new ServerSettingsService(client), metadata), ct);
    }

    /// <summary>Capture a <see cref="RolesBackup"/> from the currently connected env.</summary>
    public Task<RolesBackup> CaptureRolesBackupAsync(BackupMetadata metadata, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => RolesBackup.Capture(new RoleProvisioning(client), metadata), ct);
    }

    /// <summary>Capture a <see cref="RestrictedFieldsBackup"/> from the currently connected env.</summary>
    public Task<RestrictedFieldsBackup> CaptureRestrictedFieldsBackupAsync(BackupMetadata metadata, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => RestrictedFieldsBackup.Capture(new RestrictedFieldProvisioning(client), metadata), ct);
    }

    /// <summary>Capture an <see cref="ExtensionsBackup"/> from the currently connected env.</summary>
    public Task<ExtensionsBackup> CaptureExtensionsBackupAsync(
        EnvironmentEntry? env, EnvironmentSecret? secret, BackupMetadata metadata, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        InriverRestClient? rest = null;
        if (env is not null && secret is not null && !string.IsNullOrEmpty(env.RestBaseUrl) && !string.IsNullOrEmpty(secret.RestApiKey))
            rest = new InriverRestClient(env.RestBaseUrl!, secret.RestApiKey!);
        return Task.Run(() => ExtensionsBackup.Capture(new ExtensionsService(client, rest), metadata), ct);
    }

    /// <summary>Restore from a <see cref="UsersBackup"/> into the connected env.</summary>
    public Task<List<UserProvisioning.ProvisionResult>> RestoreUsersAsync(
        UsersBackup backup, EnvironmentSecret? secret, EnvironmentEntry env, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        InriverRestClient? rest = null;
        if (!string.IsNullOrEmpty(env.RestBaseUrl) && !string.IsNullOrEmpty(secret?.RestApiKey))
            rest = new InriverRestClient(env.RestBaseUrl!, secret.RestApiKey!);
        var prov = new UserProvisioning(client, rest);
        return backup.RestoreAsync(prov, ct);
    }

    /// <summary>Restore from a <see cref="RolesBackup"/> into the connected env. Upserts each role +
    /// permissions; the provisioning surface has no dry-run, so every call is a write.</summary>
    public Task<List<RoleProvisioning.ProvisionResult>> RestoreRolesAsync(
        RolesBackup backup, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return backup.RestoreAsync(new RoleProvisioning(client), ct);
    }

    /// <summary>Restore from a <see cref="RestrictedFieldsBackup"/> into the connected env. Adds
    /// missing restrictions (existing rows are left untouched — restrictions have no update).</summary>
    public Task<List<RestrictedFieldProvisioning.ProvisionResult>> RestoreRestrictedFieldsAsync(
        RestrictedFieldsBackup backup, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return backup.RestoreAsync(new RestrictedFieldProvisioning(client), ct);
    }

    /// <summary>Restore from a <see cref="ServerSettingsBackup"/> into the connected env.</summary>
    public Task<List<ServerSettingsBackup.RestoreEntry>> RestoreServerSettingsAsync(
        ServerSettingsBackup backup, bool dryRun = false, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return backup.RestoreAsync(new ServerSettingsService(client), dryRun, ct);
    }

    /// <summary>Restore from an <see cref="ExtensionsBackup"/> into the connected env.</summary>
    public Task<List<ExtensionsBackup.RestoreEntry>> RestoreExtensionsAsync(
        ExtensionsBackup backup, EnvironmentEntry? env, EnvironmentSecret? secret,
        bool dryRun = false, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        InriverRestClient? rest = null;
        if (env is not null && secret is not null && !string.IsNullOrEmpty(env.RestBaseUrl) && !string.IsNullOrEmpty(secret.RestApiKey))
            rest = new InriverRestClient(env.RestBaseUrl!, secret.RestApiKey!);
        return backup.RestoreAsync(new ExtensionsService(client, rest), dryRun, ct);
    }

    // ---------------- Cross-env capture (sequential connections) ----------------

    /// <summary>
    /// Disconnects the current connection (if any) and connects to <paramref name="env"/> using
    /// <paramref name="secret"/>. RemoteManager is a process-wide singleton, so this is the only
    /// way to talk to a different env without spawning a separate process. The caller's previous
    /// connection is lost; the new one becomes the active env reflected in the connection bar.
    /// </summary>
    public async Task SwitchEnvAsync(EnvironmentEntry env, EnvironmentSecret secret, CancellationToken ct = default)
    {
        await _connection.DisconnectAsync().ConfigureAwait(false);
        await _connection.ConnectAsync(env, secret, ct).ConfigureAwait(false);
        if (_connection.State != ConnectionState.Connected)
            throw new InvalidOperationException(
                $"Failed to switch to environment '{env.Name}': {_connection.LastError ?? "unknown error"}");
    }

    /// <summary>
    /// Switches to <paramref name="env"/> and captures its server-settings dictionary. Leaves the
    /// connection bound to <paramref name="env"/> on return.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> CaptureServerSettingsFromEnvAsync(
        EnvironmentEntry env, EnvironmentSecret secret, CancellationToken ct = default)
    {
        await SwitchEnvAsync(env, secret, ct).ConfigureAwait(false);
        return await ListServerSettingsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Switches to <paramref name="env"/> and captures its extension list. Leaves the connection
    /// bound to <paramref name="env"/> on return.
    /// </summary>
    public async Task<IReadOnlyList<ExtensionsService.ExtensionInfo>> CaptureExtensionsFromEnvAsync(
        EnvironmentEntry env, EnvironmentSecret secret, CancellationToken ct = default)
    {
        await SwitchEnvAsync(env, secret, ct).ConfigureAwait(false);
        return await ListExtensionsAsync(env, secret, ct).ConfigureAwait(false);
    }

    // ---------------- Work-area folders (shared) ----------------

    /// <summary>List the connected env's shared work-area folders (flat DTOs, each with its tree path).</summary>
    public Task<IReadOnlyList<WorkAreaFolderDto>> ListWorkAreasAsync(CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new WorkAreaService(client).List(), ct);
    }

    /// <summary>Create a shared work-area folder under an optional parent. Returns the new folder id.</summary>
    public Task<Guid> CreateWorkAreaFolderAsync(string name, Guid? parentId, int index, bool isQuery, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return new WorkAreaService(client).CreateFolderAsync(name, parentId, index, isQuery, ct);
    }

    /// <summary>Rename a shared work-area folder on the connected env.</summary>
    public Task RenameWorkAreaFolderAsync(Guid id, string name, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return new WorkAreaService(client).RenameFolderAsync(id, name, ct);
    }

    /// <summary>Delete a shared work-area folder (and its contents) from the connected env.</summary>
    public Task DeleteWorkAreaFolderAsync(Guid id, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return new WorkAreaService(client).DeleteFolderAsync(id, ct);
    }

    /// <summary>Apply Excel-described folders to the connected env (reconcile by path).</summary>
    public Task<WorkAreaApplyResult> ApplyWorkAreasAsync(IReadOnlyList<WorkAreaFolderDto> folders, bool allowDeletes, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return new WorkAreaService(client).ApplyAsync(folders, allowDeletes, ct);
    }

    /// <summary>Diff work-area folders against the connected env into an ordered, stateful reconcile
    /// session the import workflow drives one folder at a time (for per-row progress).</summary>
    public WorkAreaReconcileSession PlanWorkAreas(IReadOnlyList<WorkAreaFolderDto> folders, bool allowDeletes)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return new WorkAreaService(client).Plan(folders, allowDeletes);
    }

    /// <summary>Capture a <see cref="WorkAreasBackup"/> from the connected env.</summary>
    public Task<WorkAreasBackup> CaptureWorkAreasBackupAsync(BackupMetadata metadata, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => WorkAreasBackup.Capture(new WorkAreaService(client), metadata), ct);
    }

    /// <summary>Restore a <see cref="WorkAreasBackup"/> into the connected env (create/update by path).</summary>
    public Task<List<WorkAreasBackup.RestoreEntry>> RestoreWorkAreasAsync(WorkAreasBackup backup, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return backup.RestoreAsync(new WorkAreaService(client), ct);
    }

    /// <summary>Switch to <paramref name="env"/> and capture its shared work-area folders. Leaves the
    /// connection bound to <paramref name="env"/> on return.</summary>
    public async Task<IReadOnlyList<WorkAreaFolderDto>> CaptureWorkAreasFromEnvAsync(
        EnvironmentEntry env, EnvironmentSecret secret, CancellationToken ct = default)
    {
        await SwitchEnvAsync(env, secret, ct).ConfigureAwait(false);
        return await ListWorkAreasAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Faithful env→env promote of shared work-area folders. Connects to the source to read the live
    /// folders (keeping their <c>ComplexQuery</c> objects), then connects to the target and reconciles by
    /// path. Leaves the connection bound to <paramref name="targetEnv"/> on return.
    /// </summary>
    public async Task<WorkAreaApplyResult> PromoteWorkAreasAsync(
        EnvironmentEntry sourceEnv, EnvironmentSecret sourceSecret,
        EnvironmentEntry targetEnv, EnvironmentSecret targetSecret,
        bool allowDeletes, CancellationToken ct = default)
    {
        await SwitchEnvAsync(sourceEnv, sourceSecret, ct).ConfigureAwait(false);
        var srcClient = _connection.Client ?? throw new InvalidOperationException("Source connection lost.");
        var raw = await Task.Run(() => new WorkAreaService(srcClient).GetRawFolders(), ct).ConfigureAwait(false);

        await SwitchEnvAsync(targetEnv, targetSecret, ct).ConfigureAwait(false);
        var tgtClient = _connection.Client ?? throw new InvalidOperationException("Target connection lost.");
        return await new WorkAreaService(tgtClient).ApplyAsync(raw, allowDeletes, ct).ConfigureAwait(false);
    }

    // ---------------- HTML templates ----------------

    /// <summary>List the connected env's HTML templates (body included).</summary>
    public Task<IReadOnlyList<HtmlTemplateDto>> ListHtmlTemplatesAsync(CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => new HtmlTemplateService(client).List(), ct);
    }

    /// <summary>Create a new HTML template on the connected env. Returns the new id.</summary>
    public Task<int> CreateHtmlTemplateAsync(HtmlTemplateDto dto, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return new HtmlTemplateService(client).CreateAsync(dto, ct);
    }

    /// <summary>Update an existing HTML template on the connected env.</summary>
    public Task UpdateHtmlTemplateAsync(HtmlTemplateDto dto, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return new HtmlTemplateService(client).UpdateAsync(dto, ct);
    }

    /// <summary>Delete an HTML template (by live id) from the connected env.</summary>
    public Task DeleteHtmlTemplateAsync(int id, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return new HtmlTemplateService(client).DeleteAsync(id, ct);
    }

    /// <summary>Apply Excel-described templates to the connected env (reconcile by name + type).</summary>
    public Task<HtmlTemplateApplyResult> ApplyHtmlTemplatesAsync(IReadOnlyList<HtmlTemplateDto> templates, bool allowDeletes, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return new HtmlTemplateService(client).ApplyAsync(templates, allowDeletes, ct);
    }

    /// <summary>Diff templates against the connected env into an ordered action list the import workflow
    /// drives one template at a time (for per-row progress).</summary>
    public IReadOnlyList<HtmlTemplateAction> PlanHtmlTemplates(IReadOnlyList<HtmlTemplateDto> templates, bool allowDeletes)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return new HtmlTemplateService(client).Plan(templates, allowDeletes);
    }

    /// <summary>Execute a single planned HTML-template action against the connected env.</summary>
    public Task ExecuteHtmlTemplateActionAsync(HtmlTemplateAction action, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return new HtmlTemplateService(client).ExecuteAsync(action, ct);
    }

    /// <summary>Capture an <see cref="HtmlTemplatesBackup"/> from the connected env.</summary>
    public Task<HtmlTemplatesBackup> CaptureHtmlTemplatesBackupAsync(BackupMetadata metadata, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() => HtmlTemplatesBackup.Capture(new HtmlTemplateService(client), metadata), ct);
    }

    /// <summary>Restore an <see cref="HtmlTemplatesBackup"/> into the connected env (create/update by name + type).</summary>
    public Task<List<HtmlTemplatesBackup.RestoreEntry>> RestoreHtmlTemplatesAsync(HtmlTemplatesBackup backup, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        return backup.RestoreAsync(new HtmlTemplateService(client), ct);
    }

    /// <summary>Capture a <see cref="CvlsBackup"/> (CVL definitions + values) from the connected env.</summary>
    public async Task<CvlsBackup> CaptureCvlsBackupAsync(BackupMetadata metadata, CancellationToken ct = default)
    {
        var live = await CaptureSnapshotAsync(ct).ConfigureAwait(false);
        return CvlsBackup.Capture(live, metadata);
    }

    /// <summary>Restore a <see cref="CvlsBackup"/> into the connected env (create missing CVLs, upsert values).</summary>
    public async Task<List<CvlsBackup.RestoreEntry>> RestoreCvlsAsync(CvlsBackup backup, CancellationToken ct = default)
    {
        var client = _connection.Client ?? throw new InvalidOperationException("Not connected.");
        var live = await CaptureSnapshotAsync(ct).ConfigureAwait(false);
        return await backup.RestoreAsync(new ModelMeister.Inriver.Cvl.CvlAdmin(client), live.Cvls, ct).ConfigureAwait(false);
    }

    /// <summary>Switch to <paramref name="env"/> and capture its HTML templates. Leaves the connection
    /// bound to <paramref name="env"/> on return.</summary>
    public async Task<IReadOnlyList<HtmlTemplateDto>> CaptureHtmlTemplatesFromEnvAsync(
        EnvironmentEntry env, EnvironmentSecret secret, CancellationToken ct = default)
    {
        await SwitchEnvAsync(env, secret, ct).ConfigureAwait(false);
        return await ListHtmlTemplatesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Faithful env→env promote of HTML templates (matched by name + type). Connects to the
    /// source to read templates, then to the target and reconciles. Leaves the connection on the target.</summary>
    public async Task<HtmlTemplateApplyResult> PromoteHtmlTemplatesAsync(
        EnvironmentEntry sourceEnv, EnvironmentSecret sourceSecret,
        EnvironmentEntry targetEnv, EnvironmentSecret targetSecret,
        bool allowDeletes, CancellationToken ct = default)
    {
        await SwitchEnvAsync(sourceEnv, sourceSecret, ct).ConfigureAwait(false);
        var srcClient = _connection.Client ?? throw new InvalidOperationException("Source connection lost.");
        var source = await Task.Run(() => new HtmlTemplateService(srcClient).List(), ct).ConfigureAwait(false);

        await SwitchEnvAsync(targetEnv, targetSecret, ct).ConfigureAwait(false);
        var tgtClient = _connection.Client ?? throw new InvalidOperationException("Target connection lost.");
        return await new HtmlTemplateService(tgtClient).ApplyAsync(source, allowDeletes, ct).ConfigureAwait(false);
    }
}
