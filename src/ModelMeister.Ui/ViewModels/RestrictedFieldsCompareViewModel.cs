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
/// Compare restricted-field permissions across two environments. A row is fully described by its
/// natural key (role name + restriction type + entity/field/category ids), so there is no "changed"
/// state — a difference is always a row present on one side only. Promotion adds the source-side row
/// into the target env (the role name is resolved to a target role id at add time).
/// </summary>
public partial class RestrictedFieldsCompareViewModel : CompareViewModelBase<RestrictedFieldCompareRow>
{
    private readonly List<RestrictedFieldCompareRow> _allRows = new();
    public override string BucketPath => "State";

    /// <summary>Checkbox-selection model over <see cref="Rows"/>; backs the bulk Promote command.</summary>
    public RowSelectionModel Selection { get; }

    private IReadOnlyList<RestrictedFieldSummary>? _leftCapture;
    private IReadOnlyList<RestrictedFieldSummary>? _rightCapture;
    /// <summary>Total permissions compared (union of both envs), including identical ones we don't show.</summary>
    private int _comparedCount;

    protected override string CsvFileName => "restricted-fields-compare.csv";
    protected override string LogSource => "RestrictedFieldsCompare";

    public RestrictedFieldsCompareViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
        : base(main, shell, main.Vault, log)
    {
        Status = "Pick two environments to compare restricted-field permissions.";
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
            new("State",       r => ((RestrictedFieldCompareRow)r).State),
            new("Role",        r => ((RestrictedFieldCompareRow)r).RoleName),
            new("Restriction", r => ((RestrictedFieldCompareRow)r).RestrictionType),
            new("EntityType",  r => ((RestrictedFieldCompareRow)r).EntityTypeId),
            new("FieldType",   r => ((RestrictedFieldCompareRow)r).FieldTypeId),
            new("Category",    r => ((RestrictedFieldCompareRow)r).CategoryId),
            new("Detail",      r => ((RestrictedFieldCompareRow)r).Detail),
        };

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
            Status = $"Connecting to '{LeftEnv.Name}'…";
            await _shell.SwitchEnvAsync(LeftEnv, leftSecret).ConfigureAwait(true);
            Status = $"Listing restricted fields in '{LeftEnv.Name}'…";
            var left = await _shell.ListRestrictedFieldsAsync().ConfigureAwait(true);

            Status = $"Connecting to '{RightEnv.Name}'…";
            await _shell.SwitchEnvAsync(RightEnv, rightSecret).ConfigureAwait(true);
            Status = $"Listing restricted fields in '{RightEnv.Name}'…";
            var right = await _shell.ListRestrictedFieldsAsync().ConfigureAwait(true);

            _leftCapture = left;
            _rightCapture = right;
            PopulateRows(left, right);

            var diffCount = _allRows.Count;
            HasRows = diffCount > 0;
            RebuildCounts();
            Summary = diffCount == 0
                ? $"No differences. ({_comparedCount} restricted-field permissions compared.)"
                : $"{diffCount} differences across {_comparedCount} restricted-field permissions.";
            Status = "Comparison complete.";
            _log.Success("RestrictedFieldsCompare", $"Compared '{LeftEnv.Name}' ({left.Count}) vs '{RightEnv.Name}' ({right.Count}): {diffCount} differences.");
        }
        catch (Exception ex)
        {
            Status = "Compare failed: " + ex.Message;
            _log.Error("RestrictedFieldsCompare", ex.Message, ex);
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

    private void PopulateRows(IReadOnlyList<RestrictedFieldSummary> left, IReadOnlyList<RestrictedFieldSummary> right)
    {
        _allRows.Clear();
        var leftMap = left.GroupBy(r => r.NaturalKey, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var rightMap = right.GroupBy(r => r.NaturalKey, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var leftName = LeftEnv?.Name ?? "source";
        var rightName = RightEnv?.Name ?? "target";

        var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        allKeys.UnionWith(leftMap.Keys);
        allKeys.UnionWith(rightMap.Keys);
        _comparedCount = allKeys.Count;

        foreach (var key in allKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var l = leftMap.GetValueOrDefault(key);
            var r = rightMap.GetValueOrDefault(key);
            var src = l ?? r!;

            string state;
            bool canL2R, canR2L;
            string detail;
            if (l is null) { state = "only-right"; canL2R = false; canR2L = true; detail = $"only in {rightName}"; }
            else if (r is null) { state = "only-left"; canL2R = true; canR2L = false; detail = $"only in {leftName}"; }
            else continue; // identical — not a difference, never shown in compare

            _allRows.Add(new RestrictedFieldCompareRow(
                state,
                src.RoleName,
                src.RestrictionType,
                src.EntityTypeId ?? "",
                src.FieldTypeId ?? "",
                src.CategoryId ?? "",
                detail,
                canPromoteLeftToRight: canL2R,
                canPromoteRightToLeft: canR2L));
        }

        RebuildVisibleRows();
    }

    [RelayCommand]
    public Task ApplyLeftToRightAsync(RestrictedFieldCompareRow? row) => ApplyRowAsync(row, sourceFromLeft: true);

    [RelayCommand]
    public async Task PromoteSelectedLeftToRightAsync()
    {
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        var targetEnv = RightEnv;
        var rows = Selection.SelectedOf<RestrictedFieldCompareRow>().Where(r => r.CanPromoteLeftToRight).ToList();
        if (rows.Count == 0) { Status = "Select at least one promotable restricted-field permission."; return; }

        var confirmed = await DialogHost.ConfirmPromoteAsync(
            conceptLabel: "Restricted fields",
            itemLabel: $"{rows.Count} permission(s)",
            sourceEnv: LeftEnv.Name,
            targetEnv: targetEnv.Name,
            targetTypeKey: targetEnv.TypeKey).ConfigureAwait(true);
        if (!confirmed) { Status = "Promote cancelled."; return; }

        foreach (var row in rows)
            await ApplyRowAsync(row, sourceFromLeft: true, refresh: false, confirm: false).ConfigureAwait(true);
        await CompareAsync().ConfigureAwait(true);
    }

    private async Task ApplyRowAsync(RestrictedFieldCompareRow? row, bool sourceFromLeft, bool refresh = true, bool confirm = true)
    {
        if (row is null) return;
        if (LeftEnv is null || RightEnv is null) { Status = "Pick both environments first."; return; }
        if (_leftCapture is null || _rightCapture is null) { Status = "Run a compare first."; return; }

        var sourceList = sourceFromLeft ? _leftCapture : _rightCapture;
        var sourceEnv = sourceFromLeft ? LeftEnv : RightEnv;
        var targetEnv = sourceFromLeft ? RightEnv : LeftEnv;

        var key = RestrictedFieldProvisioning.NaturalKey(
            row.RoleName, row.RestrictionType,
            RestrictedFieldProvisioning.NullIfEmpty(row.EntityTypeId), RestrictedFieldProvisioning.NullIfEmpty(row.FieldTypeId), RestrictedFieldProvisioning.NullIfEmpty(row.CategoryId));
        var source = sourceList.FirstOrDefault(r => string.Equals(r.NaturalKey, key, StringComparison.OrdinalIgnoreCase));
        if (source is null) { Status = $"Source restriction not in {(sourceFromLeft ? "source" : "target")} capture."; return; }

        var targetSecret = _vault.GetSecret(targetEnv.Id);
        if (targetSecret is null || string.IsNullOrEmpty(targetSecret.ApiKey))
        { Status = $"No API key on file for target '{targetEnv.Name}'."; return; }

        if (confirm)
        {
            var confirmed = await DialogHost.ConfirmPromoteAsync(
                conceptLabel: "Restricted field",
                itemLabel: $"{source.RoleName} · {source.RestrictionType}",
                sourceEnv: sourceEnv.Name,
                targetEnv: targetEnv.Name,
                targetTypeKey: targetEnv.TypeKey).ConfigureAwait(true);
            if (!confirmed) { Status = "Promote cancelled."; return; }
        }

        Busy = true;
        Status = $"Promoting restriction '{source.RoleName} · {source.RestrictionType}' → '{targetEnv.Name}'…";
        try
        {
            if (_main.ConnectedEnv?.Id != targetEnv.Id)
                await _shell.SwitchEnvAsync(targetEnv, targetSecret).ConfigureAwait(true);

            var result = await _shell.AddRestrictedFieldAsync(new RestrictedFieldProvisioning.RestrictedFieldSpec(
                source.RoleName, source.RestrictionType,
                source.EntityTypeId, source.FieldTypeId, source.CategoryId)).ConfigureAwait(true);

            if (result.Errors.Count == 0)
            {
                Status = $"Added restriction for '{source.RoleName}' on '{targetEnv.Name}'.";
                _log.Success("RestrictedFieldsCompare", Status);
            }
            else
            {
                Status = $"Promote restriction had errors: {string.Join("; ", result.Errors)}";
                _log.Error("RestrictedFieldsCompare", Status);
            }
        }
        catch (Exception ex)
        {
            Status = "Promote failed: " + ex.Message;
            _log.Error("RestrictedFieldsCompare", ex.Message, ex);
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

public sealed partial class RestrictedFieldCompareRow : SelectableRow
{
    public RestrictedFieldCompareRow(
        string state,
        string roleName,
        string restrictionType,
        string entityTypeId,
        string fieldTypeId,
        string categoryId,
        string detail,
        bool canPromoteLeftToRight,
        bool canPromoteRightToLeft)
    {
        State = state;
        RoleName = roleName;
        RestrictionType = restrictionType;
        EntityTypeId = entityTypeId;
        FieldTypeId = fieldTypeId;
        CategoryId = categoryId;
        Detail = detail;
        CanPromoteLeftToRight = canPromoteLeftToRight;
        CanPromoteRightToLeft = canPromoteRightToLeft;
    }

    public string State { get; }
    public string RoleName { get; }
    public string RestrictionType { get; }
    public string EntityTypeId { get; }
    public string FieldTypeId { get; }
    public string CategoryId { get; }
    public string Detail { get; }
    public bool CanPromoteLeftToRight { get; }
    public bool CanPromoteRightToLeft { get; }

    /// <summary>Present in the left / right environment — drives the "Environment" pill column.</summary>
    public bool InLeft => State is "only-left" or "changed";
    public bool InRight => State is "only-right" or "changed";
}
