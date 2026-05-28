using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.ServerSettings;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// "Server settings" Compare page. Compares the flat server-settings dictionary across two saved
/// environments and lets the user promote/transfer individual keys or whole batches. Uses the
/// shared <see cref="Views.CompareLayoutView"/> chrome: env pickers w/ stage pills, auto-compare,
/// per-column FilterableHeader, bottom bar chart, Save CSV / Copy markdown.
/// </summary>
/// <remarks>
/// The Remoting singleton (see <see cref="Inriver.InriverClient"/>) restricts this process to one
/// live connection at a time, so capture is sequential. After a per-row Apply / bulk Promote the
/// app remains connected to whichever side was written to.
/// </remarks>
public partial class ServerSettingsCompareViewModel : CompareViewModelBase<ServerSettingRow>
{
    /// <inheritdoc/>
    public override bool SupportsCompare => true;
    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.ServerSettings;
    /// <inheritdoc/>
    /// <remarks>The Compare page exports only; importing/bulk-applying a workbook is a single-env
    /// mutation that belongs on the Manage sub-page (<see cref="ServerSettingsViewModel"/>),
    /// where it runs through the mandatory dry-run preview. Keeping import off here avoids a second,
    /// inconsistent (no-dry-run) write path.</remarks>
    public override ExcelCapability Excel => ExcelCapability.Export;

    /// <inheritdoc/>
    public override async Task BackupAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Backup", "Connect first."); return; }
        try
        {
            var path = await _main.Backups.CaptureServerSettingsAsync().ConfigureAwait(true);
            _log.Success("Backup", $"Server settings backup saved → {path}");
            _log.Toast(LogLevel.Success, "Backup saved", System.IO.Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"Server settings backup failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "Backup failed", ex.Message);
        }
    }

    /// <inheritdoc/>
    public override async Task ExportExcelAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Export", "Connect first."); return; }
        var path = await FilePickerHelpers.PickSaveAsync("Save server settings workbook", "serversettings.xlsx", "xlsx").ConfigureAwait(true);
        if (path is null) return;
        try
        {
            var dict = await _shell.ListServerSettingsAsync().ConfigureAwait(true);
            ModelMeister.Excel.ServerSettingsWorkbook.Save(dict, path);
            _log.Success("Export", $"Server settings exported → {path}");
            _log.Toast(LogLevel.Success, "Export saved", System.IO.Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Error("Export", ex.Message, ex);
            _log.Toast(LogLevel.Error, "Export failed", ex.Message);
        }
    }

    /// <summary>Full row set; <see cref="Rows"/> is filtered to whatever buckets are visible.</summary>
    private readonly List<ServerSettingRow> _allRows = new();
    public override string BucketPath => "State";

    /// <summary>Checkbox-selection model over <see cref="Rows"/>; backs the bulk Promote command.</summary>
    public RowSelectionModel Selection { get; }

    private IReadOnlyDictionary<string, string>? _leftCapture;
    private IReadOnlyDictionary<string, string>? _rightCapture;
    private ServerSettingsDelta? _delta;

    protected override string CsvFileName => "server-settings-compare.csv";
    protected override string LogSource => "ServerSettings";

    public ServerSettingsCompareViewModel(MainWindowViewModel main, Shell shell, IEnvironmentVault vault, IAppLog log)
        : base(main, shell, vault, log)
    {
        Status = "Pick two environments to compare.";
        Buckets.Changed += _ => RebuildVisibleRows();
        Selection = new RowSelectionModel(Rows);
        ExtraActions = new[]
        {
            new CompareAction("Promote selected →", Primary: true, PromoteSelectedLeftToRightCommand),
        };
        RefreshEnvList();
    }

    protected override IReadOnlyList<CompareExport.Column> BuildExportColumns() =>
        new CompareExport.Column[]
        {
            new("State", r => ((ServerSettingRow)r).State),
            new("Key",   r => ((ServerSettingRow)r).Key),
            new(string.IsNullOrEmpty(LeftColumnHeader)  ? "Left"  : LeftColumnHeader,  r => ((ServerSettingRow)r).LeftDisplay),
            new(string.IsNullOrEmpty(RightColumnHeader) ? "Right" : RightColumnHeader, r => ((ServerSettingRow)r).RightDisplay),
        };

    public string ActiveLabel => _main.ConnectedEnv is null ? "" : _main.ConnectedEnv.Name;

    /// <summary>Discard cached captures when either env changes so the next compare re-reads both sides.</summary>
    protected override void OnEnvSelectionChanged()
    {
        _leftCapture = null;
        _rightCapture = null;
    }

    public override async Task CompareAsync()
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
            Status = $"Capturing server settings from '{LeftEnv.Name}'…";
            _leftCapture = await _shell.CaptureServerSettingsFromEnvAsync(LeftEnv, leftSecret).ConfigureAwait(true);

            Status = $"Capturing server settings from '{RightEnv.Name}'…";
            _rightCapture = await _shell.CaptureServerSettingsFromEnvAsync(RightEnv, rightSecret).ConfigureAwait(true);

            RecomputeDelta();
            _log.Success("ServerSettings", $"Compared '{LeftEnv.Name}' vs '{RightEnv.Name}': {_delta?.TotalDifferences ?? 0} differences.");
        }
        catch (Exception ex)
        {
            Status = "Compare failed: " + ex.Message;
            _log.Error("ServerSettings", ex.Message, ex);
        }
        finally { Busy = false; _main.SuspendConnectionIndicator = false; }
    }

    private void RecomputeDelta()
    {
        Rows.Clear();
        Counts.Clear();

        if (_leftCapture is null || _rightCapture is null)
        {
            Status = _leftCapture is null && _rightCapture is null
                ? "Capture both sides to compare."
                : "Capture the remaining side to compare.";
            Summary = "";
            HasRows = false;
            return;
        }

        _delta = ServerSettingsDiff.Compute(_leftCapture, _rightCapture);
        RebuildRows();
        HasRows = Rows.Count > 0;
        RebuildCounts();
        Summary = _delta.TotalDifferences == 0
            ? $"No differences. ({_delta.UnchangedCount} keys identical.)"
            : $"{_delta.TotalDifferences} differences  ·  only in {LeftColumnHeader} {_delta.OnlyInLeft.Count}  ·  only in {RightColumnHeader} {_delta.OnlyInRight.Count}  ·  changed {_delta.Changed.Count}  ·  identical {_delta.UnchangedCount}";
        Status = "Comparison complete.";
    }

    private void RebuildRows()
    {
        _allRows.Clear();
        Rows.Clear();
        if (_leftCapture is null && _rightCapture is null) return;

        var allKeys = new HashSet<string>(StringComparer.Ordinal);
        if (_leftCapture is not null) allKeys.UnionWith(_leftCapture.Keys);
        if (_rightCapture is not null) allKeys.UnionWith(_rightCapture.Keys);

        foreach (var key in allKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var lv = _leftCapture is not null && _leftCapture.TryGetValue(key, out var l) ? l : null;
            var rv = _rightCapture is not null && _rightCapture.TryGetValue(key, out var r) ? r : null;

            string state;
            if (lv is null) state = "only-right";
            else if (rv is null) state = "only-left";
            else if (string.Equals(lv, rv, StringComparison.Ordinal)) state = "identical";
            else state = "changed";

            // Compare pages only surface differences; identical keys would drown the chart and grid.
            if (state == "identical") continue;

            _allRows.Add(new ServerSettingRow(key, lv, rv, state));
        }
        RebuildVisibleRows();
    }

    /// <summary>Project <see cref="_allRows"/> into <see cref="Rows"/> applying the bucket filter.</summary>
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

    /// <summary>Write <paramref name="row"/>'s left value into the right env (one-way; swap to reverse).</summary>
    [RelayCommand]
    public async Task ApplyLeftToRightAsync(ServerSettingRow? row)
    {
        if (row is null || RightEnv is null) return;
        await ApplyOneSideAsync(row, sourceValue: row.LeftValue, targetEnv: RightEnv, label: "left→right").ConfigureAwait(true);
    }

    /// <summary>Promote every selected setting left→right in one batch, confirming once up front.</summary>
    [RelayCommand]
    public async Task PromoteSelectedLeftToRightAsync()
    {
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        var targetEnv = RightEnv;
        var rows = Selection.SelectedOf<ServerSettingRow>().ToList();
        if (rows.Count == 0) { Status = "Select at least one setting to promote."; return; }

        var confirmed = await DialogHost.ConfirmPromoteAsync(
            conceptLabel: "Settings",
            itemLabel: $"{rows.Count} setting(s)",
            sourceEnv: LeftEnv.Name,
            targetEnv: targetEnv.Name,
            targetTypeKey: targetEnv.TypeKey).ConfigureAwait(true);
        if (!confirmed) { Status = "Promote cancelled."; return; }

        foreach (var row in rows)
            await ApplyOneSideAsync(row, row.LeftValue, targetEnv, "left→right", refresh: false, confirm: false).ConfigureAwait(true);
        await CompareAsync().ConfigureAwait(true);
    }

    private async Task ApplyOneSideAsync(ServerSettingRow row, string? sourceValue, EnvironmentEntry targetEnv, string label, bool refresh = true, bool confirm = true)
    {
        var secret = _vault.GetSecret(targetEnv.Id);
        if (secret is null || string.IsNullOrEmpty(secret.ApiKey))
        {
            Status = $"No API key on file for target '{targetEnv.Name}'."; return;
        }

        // Per-row promote needs explicit confirmation — it overwrites a setting on a (possibly
        // production) environment with no automatic rollback. Skipped when the bulk command confirmed.
        if (confirm)
        {
            var sourceEnvName = ReferenceEquals(targetEnv, RightEnv) ? LeftEnv?.Name ?? "left" : RightEnv?.Name ?? "right";
            var confirmed = await DialogHost.ConfirmPromoteAsync(
                conceptLabel: "Setting",
                itemLabel: row.Key,
                sourceEnv: sourceEnvName,
                targetEnv: targetEnv.Name,
                targetTypeKey: targetEnv.TypeKey).ConfigureAwait(true);
            if (!confirmed)
            {
                Status = "Promote cancelled.";
                return;
            }
        }

        Busy = true;
        Status = $"Applying '{row.Key}' {label} → '{targetEnv.Name}'…";
        try
        {
            if (_main.ConnectedEnv?.Id != targetEnv.Id)
                await _shell.SwitchEnvAsync(targetEnv, secret).ConfigureAwait(true);

            var ok = sourceValue is null
                ? await _shell.DeleteServerSettingAsync(row.Key).ConfigureAwait(true)
                : await _shell.SetServerSettingAsync(row.Key, sourceValue).ConfigureAwait(true);

            if (ok)
            {
                _log.Success("ServerSettings", $"{(sourceValue is null ? "Deleted" : "Set")} '{row.Key}' in '{targetEnv.Name}'.");
            }
            else
            {
                Status = $"Apply '{row.Key}' failed.";
                _log.Error("ServerSettings", $"Apply '{row.Key}' {label} returned false.");
            }
        }
        catch (Exception ex)
        {
            Status = "Apply failed: " + ex.Message;
            _log.Error("ServerSettings", ex.Message, ex);
        }
        finally { Busy = false; }

        // Re-run full compare so Rows/Counts/Summary reflect the new state (bulk refreshes once).
        if (refresh) await CompareAsync().ConfigureAwait(true);
    }

}

/// <summary>One row in the server-settings comparison grid.</summary>
public sealed partial class ServerSettingRow : SelectableRow
{
    public string Key { get; }
    public string? LeftValue { get; }
    public string? RightValue { get; }
    /// <summary>"identical" | "changed" | "only-left" | "only-right".</summary>
    public string State { get; }

    public ServerSettingRow(string key, string? left, string? right, string state)
    {
        Key = key;
        LeftValue = left;
        RightValue = right;
        State = state;
    }

    public string LeftDisplay => LeftValue ?? "(missing)";
    public string RightDisplay => RightValue ?? "(missing)";

    /// <summary>Present in the left / right environment — drives the "Environment" pill column.</summary>
    public bool InLeft => State is "only-left" or "changed";
    public bool InRight => State is "only-right" or "changed";
}
