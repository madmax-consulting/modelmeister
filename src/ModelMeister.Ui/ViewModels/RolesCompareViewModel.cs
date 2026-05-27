using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.Users;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Compare roles across two environments. Sequentially switches the active connection
/// (<see cref="Shell.SwitchEnvAsync"/>) and pulls the role list via <see cref="Shell.ListRolesAsync"/>,
/// then renders existence + description/permission deltas. Roles are matched by <i>name</i> (ids differ
/// per env); promotion upserts the source role + its permission bindings into the target env.
/// </summary>
public partial class RolesCompareViewModel : ViewModelBase, ICompareViewModel
{
    readonly MainWindowViewModel _main;
    readonly Shell _shell;
    readonly IAppLog _log;
    readonly IEnvironmentVault _vault;

    public ObservableCollection<EnvironmentEntry> AvailableEnvs { get; } = [];
    private readonly List<RoleCompareRow> _allRows = new();
    public ObservableCollection<RoleCompareRow> Rows { get; } = [];
    public ObservableCollection<ConceptDiffCount> Counts { get; } = [];

    public BucketToggleState Buckets { get; } = new();
    public string BucketPath => "State";

    [ObservableProperty] private EnvironmentEntry? _leftEnv;
    [ObservableProperty] private EnvironmentEntry? _rightEnv;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "Pick two environments to compare roles.";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasRows;
    [ObservableProperty] private string _leftColumnHeader = "";
    [ObservableProperty] private string _rightColumnHeader = "";
    [ObservableProperty] private EnvironmentStage _leftColumnStage;
    [ObservableProperty] private EnvironmentStage _rightColumnStage;

    public IAsyncRelayCommand SaveCsvCommand { get; }
    public IAsyncRelayCommand CopyMarkdownCommand { get; }
    public IReadOnlyList<CompareAction> ExtraActions { get; }
    /// <summary>Checkbox-selection model over <see cref="Rows"/>; backs the bulk Promote command.</summary>
    public RowSelectionModel Selection { get; }

    private IReadOnlyList<RoleSummary>? _leftCapture;
    private IReadOnlyList<RoleSummary>? _rightCapture;
    /// <summary>Total roles compared (union of both envs), including identical ones we don't show.</summary>
    private int _comparedCount;

    public RolesCompareViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        _vault = main.Vault;
        _vault.Changed += RefreshEnvList;
        Buckets.Changed += _ => RebuildVisibleRows();
        Selection = new RowSelectionModel(Rows);
        RefreshEnvList();

        SaveCsvCommand = CompareCommands.MakeSaveCsv(
            () => Rows, BuildExportColumns, suggestedFileName: "roles-compare.csv", log: _log, logSource: "RolesCompare");
        CopyMarkdownCommand = CompareCommands.MakeCopyMarkdown(
            () => Rows, BuildExportColumns, log: _log, logSource: "RolesCompare");

        ExtraActions = new[]
        {
            new CompareAction("Promote selected →", Primary: true, PromoteSelectedLeftToRightCommand),
        };
    }

    private IReadOnlyList<CompareExport.Column> BuildExportColumns() =>
        new CompareExport.Column[]
        {
            new("State",       r => ((RoleCompareRow)r).State),
            new("Role",        r => ((RoleCompareRow)r).RoleName),
            new("Description", r => ((RoleCompareRow)r).Description),
            new("Permissions", r => ((RoleCompareRow)r).Permissions),
            new("Detail",      r => ((RoleCompareRow)r).Detail),
        };

    public void RefreshEnvList()
    {
        var lid = LeftEnv?.Id;
        var rid = RightEnv?.Id;
        AvailableEnvs.Clear();
        foreach (var e in _vault.List().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            AvailableEnvs.Add(e);
        if (lid is { } li) LeftEnv = AvailableEnvs.FirstOrDefault(e => e.Id == li);
        if (rid is { } ri) RightEnv = AvailableEnvs.FirstOrDefault(e => e.Id == ri);

        if (LeftEnv is not null) { LeftColumnHeader = LeftEnv.Name; LeftColumnStage = LeftEnv.Stage; }
        if (RightEnv is not null) { RightColumnHeader = RightEnv.Name; RightColumnStage = RightEnv.Stage; }
    }

    partial void OnLeftEnvChanged(EnvironmentEntry? value)
    {
        LeftColumnHeader = value?.Name ?? "";
        LeftColumnStage = value?.Stage ?? EnvironmentStage.Unspecified;
        TryAutoCompare();
    }
    partial void OnRightEnvChanged(EnvironmentEntry? value)
    {
        RightColumnHeader = value?.Name ?? "";
        RightColumnStage = value?.Stage ?? EnvironmentStage.Unspecified;
        TryAutoCompare();
    }

    private void TryAutoCompare()
    {
        if (Busy) return;
        if (LeftEnv is null || RightEnv is null) return;
        if (LeftEnv.Id == RightEnv.Id)
        {
            Status = "Pick two different environments.";
            Rows.Clear();
            Counts.Clear();
            HasRows = false;
            Summary = "";
            return;
        }
        _ = CompareAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task CompareAsync()
    {
        if (Busy) return;
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        if (LeftEnv.Id == RightEnv.Id) { Status = "Pick two different environments."; return; }

        var leftSecret = _vault.GetSecret(LeftEnv.Id);
        var rightSecret = _vault.GetSecret(RightEnv.Id);
        if (leftSecret is null || string.IsNullOrEmpty(leftSecret.ApiKey))
        { Status = $"No API key on file for '{LeftEnv.Name}'."; return; }
        if (rightSecret is null || string.IsNullOrEmpty(rightSecret.ApiKey))
        { Status = $"No API key on file for '{RightEnv.Name}'."; return; }

        Busy = true;
        _main.SuspendConnectionIndicator = true; // don't flash the env indicator while we read both sides
        _allRows.Clear();
        Rows.Clear();
        Counts.Clear();
        Buckets.Reset(Counts);
        HasRows = false;
        Summary = "";
        try
        {
            Status = $"Connecting to '{LeftEnv.Name}'…";
            await _shell.SwitchEnvAsync(LeftEnv, leftSecret).ConfigureAwait(true);
            Status = $"Listing roles in '{LeftEnv.Name}'…";
            var leftRoles = await _shell.ListRolesAsync().ConfigureAwait(true);

            Status = $"Connecting to '{RightEnv.Name}'…";
            await _shell.SwitchEnvAsync(RightEnv, rightSecret).ConfigureAwait(true);
            Status = $"Listing roles in '{RightEnv.Name}'…";
            var rightRoles = await _shell.ListRolesAsync().ConfigureAwait(true);

            _leftCapture = leftRoles;
            _rightCapture = rightRoles;
            PopulateRows(leftRoles, rightRoles);

            var diffCount = _allRows.Count;
            HasRows = diffCount > 0;
            RebuildCounts();
            Summary = diffCount == 0
                ? $"No differences. ({_comparedCount} roles compared.)"
                : $"{diffCount} differences across {_comparedCount} roles.";
            Status = "Comparison complete.";
            _log.Success("RolesCompare", $"Compared '{LeftEnv.Name}' ({leftRoles.Count}) vs '{RightEnv.Name}' ({rightRoles.Count}): {diffCount} differences.");
        }
        catch (Exception ex)
        {
            Status = "Compare failed: " + ex.Message;
            _log.Error("RolesCompare", ex.Message, ex);
        }
        finally { Busy = false; _main.SuspendConnectionIndicator = false; }
    }

    private void RebuildVisibleRows()
    {
        Rows.Clear();
        foreach (var r in _allRows)
        {
            var bucketRow = Counts.FirstOrDefault(c => string.Equals(c.Key, r.State, StringComparison.OrdinalIgnoreCase));
            if (bucketRow is { IsHidden: true }) continue;
            Rows.Add(r);
        }
    }

    /// <summary>Friendly, env-named bucket label — we never surface the internal "only-left" wording.</summary>
    private string BucketLabel(string state) => state switch
    {
        "only-left"  => $"Only in {LeftEnv?.Name ?? "left"}",
        "only-right" => $"Only in {RightEnv?.Name ?? "right"}",
        "changed"    => "Changed",
        _            => state,
    };

    private void PopulateRows(IReadOnlyList<RoleSummary> leftRoles, IReadOnlyList<RoleSummary> rightRoles)
    {
        _allRows.Clear();
        var leftMap = leftRoles.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var rightMap = rightRoles.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var leftName = LeftEnv?.Name ?? "source";
        var rightName = RightEnv?.Name ?? "target";

        var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        allNames.UnionWith(leftMap.Keys);
        allNames.UnionWith(rightMap.Keys);
        _comparedCount = allNames.Count;

        foreach (var name in allNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var l = leftMap.GetValueOrDefault(name);
            var r = rightMap.GetValueOrDefault(name);

            if (l is null)
            {
                // only-target: promoting source→target would mean delete-on-target, unsupported. Hide it.
                _allRows.Add(new RoleCompareRow(name, "only-right", r?.Description ?? "", string.Join(", ", r?.Permissions ?? []),
                    $"only in {rightName}",
                    canPromoteLeftToRight: false,
                    canPromoteRightToLeft: true));
                continue;
            }
            if (r is null)
            {
                _allRows.Add(new RoleCompareRow(name, "only-left", l.Description, string.Join(", ", l.Permissions),
                    $"only in {leftName}",
                    canPromoteLeftToRight: true,
                    canPromoteRightToLeft: false));
                continue;
            }

            var diffs = new List<string>();
            if (!string.Equals(l.Description ?? "", r.Description ?? "", StringComparison.Ordinal))
                diffs.Add($"description: {Trunc(l.Description)} → {Trunc(r.Description)}");
            var leftPerms = string.Join(",", l.Permissions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            var rightPerms = string.Join(",", r.Permissions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            if (!string.Equals(leftPerms, rightPerms, StringComparison.Ordinal))
                diffs.Add($"permissions: [{leftPerms}] → [{rightPerms}]");

            if (diffs.Count == 0) continue; // identical — not a difference, never shown in compare
            _allRows.Add(new RoleCompareRow(
                name,
                "changed",
                l.Description ?? "",
                leftPerms,
                string.Join(" · ", diffs),
                canPromoteLeftToRight: true,
                canPromoteRightToLeft: true));
        }

        RebuildVisibleRows();
    }

    private static string Trunc(string? s) => string.IsNullOrEmpty(s) ? "—" : (s.Length > 40 ? s[..40] + "…" : s);

    /// <summary>Promote <paramref name="row"/>'s source-side role into the target env (one-way; swap to reverse).</summary>
    [RelayCommand]
    public Task ApplyLeftToRightAsync(RoleCompareRow? row) => ApplyRoleAsync(row, sourceFromLeft: true);

    [RelayCommand]
    public async Task PromoteSelectedLeftToRightAsync()
    {
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        var targetEnv = RightEnv;
        var rows = Selection.SelectedOf<RoleCompareRow>().Where(r => r.CanPromoteLeftToRight).ToList();
        if (rows.Count == 0) { Status = "Select at least one promotable role."; return; }

        var confirmed = await DialogHost.ConfirmPromoteAsync(
            conceptLabel: "Roles",
            itemLabel: $"{rows.Count} role(s)",
            sourceEnv: LeftEnv.Name,
            targetEnv: targetEnv.Name,
            targetStage: targetEnv.Stage.ToString()).ConfigureAwait(true);
        if (!confirmed) { Status = "Promote cancelled."; return; }

        foreach (var row in rows)
            await ApplyRoleAsync(row, sourceFromLeft: true, refresh: false, confirm: false).ConfigureAwait(true);
        await CompareAsync().ConfigureAwait(true);
    }

    private async Task ApplyRoleAsync(RoleCompareRow? row, bool sourceFromLeft, bool refresh = true, bool confirm = true)
    {
        if (row is null) return;
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        if (_leftCapture is null || _rightCapture is null) { Status = "Run a compare first."; return; }

        var sourceList = sourceFromLeft ? _leftCapture : _rightCapture;
        var sourceEnv = sourceFromLeft ? LeftEnv : RightEnv;
        var targetEnv = sourceFromLeft ? RightEnv : LeftEnv;

        var source = sourceList.FirstOrDefault(r => string.Equals(r.Name, row.RoleName, StringComparison.OrdinalIgnoreCase));
        if (source is null) { Status = $"Source role '{row.RoleName}' not in {(sourceFromLeft ? "source" : "target")} capture."; return; }

        var targetSecret = _vault.GetSecret(targetEnv.Id);
        if (targetSecret is null || string.IsNullOrEmpty(targetSecret.ApiKey))
        { Status = $"No API key on file for target '{targetEnv.Name}'."; return; }

        if (confirm)
        {
            var confirmed = await DialogHost.ConfirmPromoteAsync(
                conceptLabel: "Role",
                itemLabel: row.RoleName,
                sourceEnv: sourceEnv.Name,
                targetEnv: targetEnv.Name,
                targetStage: targetEnv.Stage.ToString()).ConfigureAwait(true);
            if (!confirmed) { Status = "Promote cancelled."; return; }
        }

        Busy = true;
        Status = $"Promoting role '{row.RoleName}' → '{targetEnv.Name}'…";
        try
        {
            if (_main.ConnectedEnv?.Id != targetEnv.Id)
                await _shell.SwitchEnvAsync(targetEnv, targetSecret).ConfigureAwait(true);

            var result = await _shell.ProvisionRoleAsync(
                new RoleProvisioning.RoleSpec(source.Name, source.Description, source.Permissions)).ConfigureAwait(true);

            if (result.Errors.Count == 0)
            {
                Status = result.Created
                    ? $"Created role '{row.RoleName}' on '{targetEnv.Name}'."
                    : $"Synced role '{row.RoleName}' on '{targetEnv.Name}'.";
                _log.Success("RolesCompare", Status);
            }
            else
            {
                Status = $"Promote role '{row.RoleName}' had errors: {string.Join("; ", result.Errors)}";
                _log.Error("RolesCompare", Status);
            }
        }
        catch (Exception ex)
        {
            Status = "Promote failed: " + ex.Message;
            _log.Error("RolesCompare", ex.Message, ex);
        }
        finally { Busy = false; }

        if (refresh) await CompareAsync().ConfigureAwait(true);
    }

    private void RebuildCounts()
    {
        var max = 0;
        var groups = _allRows.GroupBy(r => r.State)
                         .Select(g => (State: g.Key, Count: g.Count()))
                         .OrderByDescending(t => t.Count)
                         .ToList();
        foreach (var g in groups) if (g.Count > max) max = g.Count;
        foreach (var g in groups)
            Counts.Add(new ConceptDiffCount(BucketLabel(g.State), g.Count, max == 0 ? 0 : (double)g.Count / max, key: g.State));
    }
}

public sealed partial class RoleCompareRow : SelectableRow
{
    public RoleCompareRow(
        string roleName,
        string state,
        string description,
        string permissions,
        string detail,
        bool canPromoteLeftToRight,
        bool canPromoteRightToLeft)
    {
        RoleName = roleName;
        State = state;
        Description = description;
        Permissions = permissions;
        Detail = detail;
        CanPromoteLeftToRight = canPromoteLeftToRight;
        CanPromoteRightToLeft = canPromoteRightToLeft;
    }

    public string RoleName { get; }
    public string State { get; }
    public string Description { get; }
    public string Permissions { get; }
    public string Detail { get; }
    /// <summary>Present in the left / right environment — drives the "Environment" pill column.</summary>
    public bool InLeft => State is "only-left" or "changed";
    public bool InRight => State is "only-right" or "changed";
    public bool CanPromoteLeftToRight { get; }
    public bool CanPromoteRightToLeft { get; }
}
