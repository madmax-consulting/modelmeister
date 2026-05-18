using System;
using System.Collections;
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
public partial class ServerSettingsViewModel : FeaturePageViewModel, ICompareViewModel
{
    /// <inheritdoc/>
    public override bool SupportsCompare => true;
    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.ServerSettings;
    /// <inheritdoc/>
    public override ExcelCapability Excel => ExcelCapability.ExportImport;

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
            _log.Error("Backup", $"Server settings backup failed: {ex.Message}");
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
            _log.Error("Export", ex.Message);
            _log.Toast(LogLevel.Error, "Export failed", ex.Message);
        }
    }

    /// <inheritdoc/>
    public override async Task ImportExcelAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Import", "Connect first."); return; }
        var path = await FilePickerHelpers.PickOpenAsync("Open server settings workbook", "xlsx").ConfigureAwait(true);
        if (path is null) return;
        try
        {
            var dict = ModelMeister.Excel.ServerSettingsWorkbook.Load(path);
            var entries = dict.Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value));
            var result = await _shell.BulkApplyServerSettingsAsync(entries).ConfigureAwait(true);
            _log.Success("Import", $"Applied {result.Applied.Count} keys, {result.Failed.Count} failed.");
            _log.Toast(LogLevel.Success, "Import complete", $"{result.Applied.Count} keys applied");
        }
        catch (Exception ex)
        {
            _log.Error("Import", ex.Message);
            _log.Toast(LogLevel.Error, "Import failed", ex.Message);
        }
    }

    private readonly MainWindowViewModel _main;
    private readonly Shell _shell;
    private readonly IEnvironmentVault _vault;
    private readonly IAppLog _log;

    public ObservableCollection<EnvironmentEntry> AvailableEnvs { get; } = [];
    public ObservableCollection<ServerSettingRow> Rows { get; } = [];
    public ObservableCollection<ConceptDiffCount> Counts { get; } = [];

    [ObservableProperty] private EnvironmentEntry? _leftEnv;
    [ObservableProperty] private EnvironmentEntry? _rightEnv;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "Pick two environments to compare.";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasRows;
    [ObservableProperty] private string _leftColumnHeader = "";
    [ObservableProperty] private string _rightColumnHeader = "";
    [ObservableProperty] private EnvironmentStage _leftColumnStage;
    [ObservableProperty] private EnvironmentStage _rightColumnStage;

    public IAsyncRelayCommand SaveCsvCommand { get; }
    public IAsyncRelayCommand CopyMarkdownCommand { get; }
    public IReadOnlyList<CompareAction> ExtraActions { get; }

    private IReadOnlyDictionary<string, string>? _leftCapture;
    private IReadOnlyDictionary<string, string>? _rightCapture;
    private ServerSettingsDelta? _delta;

    public ServerSettingsViewModel(MainWindowViewModel main, Shell shell, IEnvironmentVault vault, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _vault = vault;
        _log = log;
        _vault.Changed += RefreshEnvList;
        RefreshEnvList();

        SaveCsvCommand = CompareCommands.MakeSaveCsv(
            () => Rows,
            BuildExportColumns,
            suggestedFileName: "server-settings-compare.csv",
            log: _log,
            logSource: "ServerSettings");

        CopyMarkdownCommand = CompareCommands.MakeCopyMarkdown(
            () => Rows,
            BuildExportColumns,
            log: _log,
            logSource: "ServerSettings");

        ExtraActions = Array.Empty<CompareAction>();
    }

    private IReadOnlyList<CompareExport.Column> BuildExportColumns() =>
        new CompareExport.Column[]
        {
            new("State", r => ((ServerSettingRow)r).State),
            new("Key",   r => ((ServerSettingRow)r).Key),
            new(string.IsNullOrEmpty(LeftColumnHeader)  ? "Left"  : LeftColumnHeader,  r => ((ServerSettingRow)r).LeftDisplay),
            new(string.IsNullOrEmpty(RightColumnHeader) ? "Right" : RightColumnHeader, r => ((ServerSettingRow)r).RightDisplay),
        };

    /// <summary>Refresh the env dropdowns from the vault. Call after the user edits the vault.</summary>
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

    public string ActiveLabel => _main.ConnectedEnv is null ? "" : _main.ConnectedEnv.Name;

    partial void OnLeftEnvChanged(EnvironmentEntry? value)
    {
        _leftCapture = null;
        LeftColumnHeader = value?.Name ?? "";
        LeftColumnStage = value?.Stage ?? EnvironmentStage.Unspecified;
        TryAutoCompare();
    }

    partial void OnRightEnvChanged(EnvironmentEntry? value)
    {
        _rightCapture = null;
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
        Rows.Clear();
        Counts.Clear();
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
            _log.Error("ServerSettings", ex.Message);
        }
        finally { Busy = false; }
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
            : $"{_delta.TotalDifferences} differences  ·  only-left {_delta.OnlyInLeft.Count}  ·  only-right {_delta.OnlyInRight.Count}  ·  changed {_delta.Changed.Count}  ·  identical {_delta.UnchangedCount}";
        Status = "Comparison complete.";
    }

    private void RebuildRows()
    {
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

            Rows.Add(new ServerSettingRow(key, lv, rv, state));
        }
    }

    private void RebuildCounts()
    {
        var max = 0;
        var groups = Rows.GroupBy(r => r.State)
                         .Select(g => (Title: g.Key, Count: g.Count()))
                         .OrderByDescending(t => t.Count)
                         .ToList();
        foreach (var g in groups) if (g.Count > max) max = g.Count;
        foreach (var g in groups)
            Counts.Add(new ConceptDiffCount(g.Title, g.Count, max == 0 ? 0 : (double)g.Count / max));
    }

    /// <summary>Write <paramref name="row"/>'s left value into the right env (currently active).</summary>
    [RelayCommand]
    public async Task ApplyLeftToRightAsync(ServerSettingRow? row)
    {
        if (row is null || RightEnv is null) return;
        await ApplyOneSideAsync(row, sourceValue: row.LeftValue, targetEnv: RightEnv, label: "left→right").ConfigureAwait(true);
    }

    /// <summary>Write <paramref name="row"/>'s right value into the left env (will switch connection first).</summary>
    [RelayCommand]
    public async Task ApplyRightToLeftAsync(ServerSettingRow? row)
    {
        if (row is null || LeftEnv is null) return;
        await ApplyOneSideAsync(row, sourceValue: row.RightValue, targetEnv: LeftEnv, label: "right→left").ConfigureAwait(true);
    }

    private async Task ApplyOneSideAsync(ServerSettingRow row, string? sourceValue, EnvironmentEntry targetEnv, string label)
    {
        var secret = _vault.GetSecret(targetEnv.Id);
        if (secret is null || string.IsNullOrEmpty(secret.ApiKey))
        {
            Status = $"No API key on file for target '{targetEnv.Name}'."; return;
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
            _log.Error("ServerSettings", ex.Message);
        }
        finally { Busy = false; }

        // Re-run full compare so Rows/Counts/Summary reflect the new state.
        await CompareAsync().ConfigureAwait(true);
    }

}

/// <summary>One row in the server-settings comparison grid.</summary>
public sealed class ServerSettingRow
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
}
