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
/// Compare users across two environments. Sequentially switches the active connection
/// (<see cref="Shell.SwitchEnvAsync"/>) and pulls the user list via
/// <see cref="Shell.ListUsersAsync"/>, then renders existence + email/role/active deltas.
/// Read-only — provisioning lives on the single-env Manage view.
/// </summary>
public partial class UsersCompareViewModel : ViewModelBase, ICompareViewModel
{
    readonly MainWindowViewModel _main;
    readonly Shell _shell;
    readonly IAppLog _log;
    readonly IEnvironmentVault _vault;

    public ObservableCollection<EnvironmentEntry> AvailableEnvs { get; } = [];
    /// <summary>Full row set — drives the bucket counts. Rows are projected into <see cref="Rows"/> through the bucket filter.</summary>
    private readonly List<UserCompareRow> _allRows = new();
    public ObservableCollection<UserCompareRow> Rows { get; } = [];
    public ObservableCollection<ConceptDiffCount> Counts { get; } = [];

    /// <summary>Bottom-chart bucket toggle. Hiding a bucket removes its rows from <see cref="Rows"/>.</summary>
    public BucketToggleState Buckets { get; } = new();
    public string BucketPath => "State";

    [ObservableProperty] private EnvironmentEntry? _leftEnv;
    [ObservableProperty] private EnvironmentEntry? _rightEnv;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "Pick two environments to compare users.";
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

    // Cached captures so per-row promote can look up the source UserSummary without re-querying.
    private IReadOnlyList<UserSummary>? _leftCapture;
    private IReadOnlyList<UserSummary>? _rightCapture;
    /// <summary>Total users compared (union of both envs), including identical ones we don't show.</summary>
    private int _comparedCount;

    public UsersCompareViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
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
            () => Rows,
            BuildExportColumns,
            suggestedFileName: "users-compare.csv",
            log: _log,
            logSource: "UsersCompare");

        CopyMarkdownCommand = CompareCommands.MakeCopyMarkdown(
            () => Rows,
            BuildExportColumns,
            log: _log,
            logSource: "UsersCompare");

        ExtraActions = new[]
        {
            new CompareAction("Promote selected →", Primary: true, PromoteSelectedLeftToRightCommand),
        };
    }

    private IReadOnlyList<CompareExport.Column> BuildExportColumns() =>
        new CompareExport.Column[]
        {
            new("State",    r => ((UserCompareRow)r).State),
            new("Username", r => ((UserCompareRow)r).Username),
            new("Email",    r => ((UserCompareRow)r).Email),
            new("Roles",    r => ((UserCompareRow)r).Roles),
            new("Detail",   r => ((UserCompareRow)r).Detail),
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

    [RelayCommand]
    public async Task CompareAsync()
    {
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
            Status = $"Listing users in '{LeftEnv.Name}'…";
            var leftUsers = await _shell.ListUsersAsync().ConfigureAwait(true);

            Status = $"Connecting to '{RightEnv.Name}'…";
            await _shell.SwitchEnvAsync(RightEnv, rightSecret).ConfigureAwait(true);
            Status = $"Listing users in '{RightEnv.Name}'…";
            var rightUsers = await _shell.ListUsersAsync().ConfigureAwait(true);

            _leftCapture = leftUsers;
            _rightCapture = rightUsers;
            PopulateRows(leftUsers, rightUsers);

            var diffCount = _allRows.Count;
            HasRows = diffCount > 0;
            RebuildCounts();
            Summary = diffCount == 0
                ? $"No differences. ({_comparedCount} users compared.)"
                : $"{diffCount} differences across {_comparedCount} users.";
            Status = "Comparison complete.";
            _log.Success("UsersCompare", $"Compared '{LeftEnv.Name}' ({leftUsers.Count}) vs '{RightEnv.Name}' ({rightUsers.Count}): {diffCount} differences.");
        }
        catch (Exception ex)
        {
            Status = "Compare failed: " + ex.Message;
            _log.Error("UsersCompare", ex.Message, ex);
        }
        finally { Busy = false; _main.SuspendConnectionIndicator = false; }
    }

    /// <summary>Re-project <see cref="_allRows"/> into <see cref="Rows"/> after the bucket filter changes.</summary>
    private void RebuildVisibleRows()
    {
        Rows.Clear();
        foreach (var r in _allRows)
        {
            // BucketToggleState owns the hidden set internally; expose via Toggle/IsHidden flags.
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

    private void PopulateRows(IReadOnlyList<UserSummary> leftUsers, IReadOnlyList<UserSummary> rightUsers)
    {
        _allRows.Clear();
        var leftMap = leftUsers.ToDictionary(u => u.Username, StringComparer.OrdinalIgnoreCase);
        var rightMap = rightUsers.ToDictionary(u => u.Username, StringComparer.OrdinalIgnoreCase);

        var leftName = LeftEnv?.Name ?? "left";
        var rightName = RightEnv?.Name ?? "right";

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
                // only-right: L→R would mean "delete on right", which Remoting cannot do. Hide it.
                _allRows.Add(new UserCompareRow(name, "only-right", r?.Email ?? "", string.Join(", ", r?.Roles ?? []),
                    $"only in {rightName}",
                    canPromoteLeftToRight: false,
                    canPromoteRightToLeft: true));
                continue;
            }
            if (r is null)
            {
                // only-left: R→L would mean "delete on left" — same Remoting limitation, hide it.
                _allRows.Add(new UserCompareRow(name, "only-left", l.Email ?? "", string.Join(", ", l.Roles),
                    $"only in {leftName}",
                    canPromoteLeftToRight: true,
                    canPromoteRightToLeft: false));
                continue;
            }

            var diffs = new List<string>();
            if (!string.Equals(l.Email, r.Email, StringComparison.OrdinalIgnoreCase))
                diffs.Add($"email: {l.Email ?? "—"} → {r.Email ?? "—"}");
            if (l.Active != r.Active)
                diffs.Add($"active: {l.Active} → {r.Active}");
            var leftRoles = string.Join(",", l.Roles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            var rightRoles = string.Join(",", r.Roles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            if (!string.Equals(leftRoles, rightRoles, StringComparison.Ordinal))
                diffs.Add($"roles: [{leftRoles}] → [{rightRoles}]");

            if (diffs.Count == 0) continue; // identical — not a difference, never shown in compare
            _allRows.Add(new UserCompareRow(
                name,
                "changed",
                l.Email ?? "",
                leftRoles,
                string.Join(" · ", diffs),
                canPromoteLeftToRight: true,
                canPromoteRightToLeft: true));
        }

        RebuildVisibleRows();
    }

    /// <summary>Promote <paramref name="row"/>'s left-side user into the right env (one-way; swap to reverse).</summary>
    [RelayCommand]
    public Task ApplyLeftToRightAsync(UserCompareRow? row) =>
        ApplyUserAsync(row, sourceFromLeft: true);

    /// <summary>Promote every selected (changed) user left→right in one batch, confirming once up front.</summary>
    [RelayCommand]
    public async Task PromoteSelectedLeftToRightAsync()
    {
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        var targetEnv = RightEnv;
        var rows = Selection.SelectedOf<UserCompareRow>()
            .Where(r => r.CanPromoteLeftToRight)
            .ToList();
        if (rows.Count == 0) { Status = "Select at least one promotable user."; return; }

        var confirmed = await DialogHost.ConfirmPromoteAsync(
            conceptLabel: "Users",
            itemLabel: $"{rows.Count} user(s)",
            sourceEnv: LeftEnv.Name,
            targetEnv: targetEnv.Name,
            targetStage: targetEnv.Stage.ToString()).ConfigureAwait(true);
        if (!confirmed) { Status = "Promote cancelled."; return; }

        foreach (var row in rows)
            await ApplyUserAsync(row, sourceFromLeft: true, refresh: false, confirm: false).ConfigureAwait(true);
        await CompareAsync().ConfigureAwait(true);
    }

    private async Task ApplyUserAsync(UserCompareRow? row, bool sourceFromLeft, bool refresh = true, bool confirm = true)
    {
        if (row is null) return;
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        if (_leftCapture is null || _rightCapture is null) { Status = "Run a compare first."; return; }

        var sourceList = sourceFromLeft ? _leftCapture : _rightCapture;
        var sourceEnv = sourceFromLeft ? LeftEnv : RightEnv;
        var targetEnv = sourceFromLeft ? RightEnv : LeftEnv;
        var label = sourceFromLeft ? "source→target" : "target→source";

        var source = sourceList.FirstOrDefault(u => string.Equals(u.Username, row.Username, StringComparison.OrdinalIgnoreCase));
        if (source is null) { Status = $"Source user '{row.Username}' not in {(sourceFromLeft ? "source" : "target")} capture."; return; }

        var targetSecret = _vault.GetSecret(targetEnv.Id);
        if (targetSecret is null || string.IsNullOrEmpty(targetSecret.ApiKey))
        { Status = $"No API key on file for target '{targetEnv.Name}'."; return; }

        // Per-row promote is destructive — confirm before doing it (skipped when the bulk command
        // already confirmed once). Production targets get the red banner inside the dialog.
        if (confirm)
        {
            var confirmed = await DialogHost.ConfirmPromoteAsync(
                conceptLabel: "User",
                itemLabel: row.Username,
                sourceEnv: sourceEnv.Name,
                targetEnv: targetEnv.Name,
                targetStage: targetEnv.Stage.ToString()).ConfigureAwait(true);
            if (!confirmed)
            {
                Status = "Promote cancelled.";
                return;
            }
        }

        Busy = true;
        Status = $"Promoting '{row.Username}' → '{targetEnv.Name}'…";
        try
        {
            if (_main.ConnectedEnv?.Id != targetEnv.Id)
                await _shell.SwitchEnvAsync(targetEnv, targetSecret).ConfigureAwait(true);

            var spec = new UserProvisioning.UserSpec(
                Username: source.Username,
                Email: source.Email,
                FirstName: source.FirstName,
                LastName: source.LastName,
                Company: null,
                Roles: source.Roles);
            var result = await _shell.ProvisionUserAsync(spec, targetSecret, targetEnv).ConfigureAwait(true);

            if (result.Errors.Count == 0)
            {
                _log.Success("UsersCompare",
                    $"Promoted '{row.Username}' {label}: created={result.Created}, rolesAssigned={result.RolesAssigned}.");
                Status = result.Created
                    ? $"Created '{row.Username}' on '{targetEnv.Name}'."
                    : $"Synced roles for '{row.Username}' on '{targetEnv.Name}' (email/name not updated by Remoting).";
            }
            else
            {
                Status = $"Promote '{row.Username}' had errors: {string.Join("; ", result.Errors)}";
                _log.Error("UsersCompare", Status);
            }
        }
        catch (Exception ex)
        {
            Status = "Promote failed: " + ex.Message;
            _log.Error("UsersCompare", ex.Message, ex);
        }
        finally { Busy = false; }

        // Re-run the compare so rows reflect the new state (skipped during bulk — caller refreshes once).
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

public sealed partial class UserCompareRow : SelectableRow
{
    public UserCompareRow(
        string username,
        string state,
        string email,
        string roles,
        string detail,
        bool canPromoteLeftToRight,
        bool canPromoteRightToLeft)
    {
        Username = username;
        State = state;
        Email = email;
        Roles = roles;
        Detail = detail;
        CanPromoteLeftToRight = canPromoteLeftToRight;
        CanPromoteRightToLeft = canPromoteRightToLeft;
    }

    public string Username { get; }
    public string State { get; }
    public string Email { get; }
    public string Roles { get; }
    public string Detail { get; }
    public bool CanPromoteLeftToRight { get; }
    public bool CanPromoteRightToLeft { get; }

    /// <summary>Present in the left / right environment — drives the "Environment" pill column.</summary>
    public bool InLeft => State is "only-left" or "changed";
    public bool InRight => State is "only-right" or "changed";
}
