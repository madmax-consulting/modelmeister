using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Excel;
using ModelMeister.Inriver.HtmlTemplates;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// HTML Templates page view-model. Lists the connected env's print / ContentStore templates with an
/// inline editor for the body + properties, plus create/delete, Excel export/import, and env↔env
/// compare. Operational config — talks to the env directly, like Users/Extensions.
/// </summary>
public partial class HtmlTemplatesViewModel : FeaturePageViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly Shell _shell;
    private readonly IAppLog _log;

    /// <inheritdoc/>
    public override bool SupportsCompare => true;
    /// <inheritdoc/>
    public override BackupScope BackupScope => BackupScope.HtmlTemplates;
    /// <inheritdoc/>
    public override ExcelCapability Excel => ExcelCapability.ExportImport;

    /// <inheritdoc/>
    public override async Task BackupAsync()
    {
        if (!_main.IsConnected) { _log.Toast(LogLevel.Warn, "Backup", "Connect first."); return; }
        try
        {
            var path = await _main.Backups.CaptureHtmlTemplatesAsync().ConfigureAwait(true);
            _log.Success("Backup", $"HTML-templates backup saved → {path}");
            _log.Toast(LogLevel.Success, "HTML-templates backup saved", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"HTML-templates backup failed: {ex.Message}", ex);
            _log.Toast(LogLevel.Error, "HTML-templates backup failed", ex.Message);
        }
    }

    public ObservableCollection<HtmlTemplateRow> Items { get; } = [];
    public RowSelectionModel Selection { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewTemplateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private bool _busy;

    [ObservableProperty] private string _status = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private HtmlTemplateRow? _selected;

    // ----- inline editor state -----
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private int _editingId;        // 0 = new (not yet persisted)
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _editName = "";
    [ObservableProperty] private string _editType = "";
    [ObservableProperty] private string _editProperties = "";
    [ObservableProperty] private string _editContent = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    private bool _isDirty;

    private Dictionary<string, string> _editLocalizedName = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressDirty;

    public bool HasSelection => Selected is not null;
    public string EditorTitle => EditingId == 0 ? "New template" : (IsDirty ? "Editing (unsaved)" : "Template");
    public int EditContentLength => EditContent.Length;

    public HtmlTemplatesViewModel(MainWindowViewModel main, Shell shell, IAppLog log)
    {
        _main = main;
        _shell = shell;
        _log = log;
        Selection = new RowSelectionModel(Items);
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsConnected))
            {
                MarkDataDirty();
                if (_main.IsConnected) _ = EnsureLoadedAsync();
                NewTemplateCommand.NotifyCanExecuteChanged();
            }
        };
    }

    private bool CanMutate() => !Busy && _main.IsConnected;
    private bool CanSave() => !Busy && _main.IsConnected && IsEditing && !string.IsNullOrWhiteSpace(EditName);

    /// <inheritdoc/>
    public override async Task RefreshAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        Busy = true;
        try
        {
            var templates = await _shell.ListHtmlTemplatesAsync().ConfigureAwait(true);
            var keepName = Selected?.Name;
            Items.Clear();
            foreach (var t in templates) Items.Add(new HtmlTemplateRow(t));
            Status = templates.Count == 0 ? "No HTML templates." : $"{templates.Count} template(s)";
            // Re-select by name after a refresh so an edit keeps focus.
            if (keepName is not null)
                Selected = Items.FirstOrDefault(i => string.Equals(i.Name, keepName, StringComparison.Ordinal));
            else if (!IsEditing) ClearEditor();
        }
        catch (Exception ex)
        {
            Status = "Failed: " + ex.Message;
            _log.Error("HtmlTemplates", ex.Message, ex);
        }
        finally { Busy = false; }
    }

    partial void OnSelectedChanged(HtmlTemplateRow? value)
    {
        if (value is null) return;
        LoadEditor(value.Source);
    }

    private void LoadEditor(HtmlTemplateDto dto)
    {
        _suppressDirty = true;
        EditingId = dto.Id;
        EditName = dto.Name;
        EditType = dto.TemplateType;
        EditProperties = dto.Properties;
        EditContent = dto.Content;
        _editLocalizedName = new Dictionary<string, string>(dto.LocalizedName, StringComparer.OrdinalIgnoreCase);
        IsEditing = true;
        IsDirty = false;
        _suppressDirty = false;
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditContentLength));
    }

    private void ClearEditor()
    {
        _suppressDirty = true;
        IsEditing = false;
        EditingId = 0;
        EditName = EditType = EditProperties = EditContent = "";
        _editLocalizedName = new(StringComparer.OrdinalIgnoreCase);
        IsDirty = false;
        _suppressDirty = false;
    }

    partial void OnEditNameChanged(string value) => MarkDirty();
    partial void OnEditTypeChanged(string value) => MarkDirty();
    partial void OnEditPropertiesChanged(string value) => MarkDirty();
    partial void OnEditContentChanged(string value) { MarkDirty(); OnPropertyChanged(nameof(EditContentLength)); }

    private void MarkDirty()
    {
        if (_suppressDirty) return;
        IsDirty = true;
    }

    /// <summary>Start a new, unsaved template in the editor.</summary>
    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void NewTemplate()
    {
        Selected = null;
        ClearEditor();
        IsEditing = true;
        OnPropertyChanged(nameof(EditorTitle));
    }

    /// <summary>Persist the editor — create when new, update otherwise.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!_main.IsConnected) { Status = "Connect first."; return; }
        var dto = new HtmlTemplateDto
        {
            Id = EditingId,
            Name = EditName.Trim(),
            TemplateType = EditType.Trim(),
            Properties = EditProperties,
            Content = EditContent,
            LocalizedName = _editLocalizedName,
        };
        Busy = true;
        Status = $"Saving '{dto.Name}'…";
        try
        {
            if (EditingId == 0)
            {
                await _shell.CreateHtmlTemplateAsync(dto).ConfigureAwait(true);
                _log.Success("HtmlTemplates", $"Created template '{dto.Name}'.");
            }
            else
            {
                await _shell.UpdateHtmlTemplateAsync(dto).ConfigureAwait(true);
                _log.Success("HtmlTemplates", $"Updated template '{dto.Name}'.");
            }
            IsDirty = false;
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
            Selected = Items.FirstOrDefault(i => string.Equals(i.Name, dto.Name, StringComparison.Ordinal));
            Status = $"Saved '{dto.Name}'.";
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("HtmlTemplates", ex.Message, ex); }
        finally { Busy = false; }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        if (Selected is not null) LoadEditor(Selected.Source);
        else ClearEditor();
    }

    /// <summary>Delete a single template after confirmation.</summary>
    [RelayCommand]
    private async Task DeleteAsync(HtmlTemplateRow? row)
    {
        row ??= Selected;
        if (row is null || !_main.IsConnected) return;
        await ConfirmAndDeleteAsync(new[] { row }).ConfigureAwait(true);
    }

    /// <summary>Delete every checked template after one itemized confirmation.</summary>
    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task DeleteSelectedAsync()
    {
        var rows = Selection.SelectedOf<HtmlTemplateRow>();
        if (rows.Count == 0) { Status = "Select at least one template."; return; }
        await ConfirmAndDeleteAsync(rows).ConfigureAwait(true);
    }

    private async Task ConfirmAndDeleteAsync(IReadOnlyList<HtmlTemplateRow> rows)
    {
        var ok = await DialogHost.ConfirmBulkAsync(
            "Delete HTML templates", "Delete", "template",
            rows.Select(r => r.Name).ToList(),
            _main.ConnectedEnv?.Name, _main.ConnectedEnv?.Stage ?? Models.EnvironmentStage.Unspecified).ConfigureAwait(true);
        if (!ok) return;
        Busy = true;
        int deleted = 0, errors = 0;
        try
        {
            await RunBulkAsync(rows,
                async r =>
                {
                    try { await _shell.DeleteHtmlTemplateAsync(r.Source.Id).ConfigureAwait(false); deleted++; }
                    catch (Exception ex) { errors++; _log.Warn("HtmlTemplates", $"Delete '{r.Name}' failed: {ex.Message}"); }
                },
                (i, total, r) => Status = $"Deleting template {i} / {total} ('{r.Name}')…").ConfigureAwait(true);
            Status = errors == 0 ? $"Deleted {deleted} template(s)." : $"Deleted {deleted}, {errors} failed.";
            _log.Success("HtmlTemplates", Status);
            ClearEditor();
            MarkDataDirty();
            await RefreshAsync().ConfigureAwait(true);
        }
        finally { Busy = false; }
    }

    [RelayCommand] private Task CopyName(HtmlTemplateRow? r) => ClipboardHelpers.CopyAsync(r?.Name);
    [RelayCommand] private Task CopyContent(HtmlTemplateRow? r) => ClipboardHelpers.CopyAsync(r?.Source.Content);

    // ----- Excel -----

    /// <inheritdoc/>
    public override async Task ExportExcelAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        var path = await FilePickerHelpers.PickSaveAsync("Save HTML templates workbook", "htmltemplates.xlsx", "xlsx").ConfigureAwait(true);
        if (path is null) return;
        Busy = true;
        try
        {
            var templates = await _shell.ListHtmlTemplatesAsync().ConfigureAwait(true);
            await Task.Run(() => HtmlTemplateWorkbook.Save(templates, path)).ConfigureAwait(true);
            Status = $"Wrote {Path.GetFileName(path)} · {templates.Count} template(s)";
            _log.Success("HtmlTemplates", $"Exported {templates.Count} template(s) → {path}");
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; _log.Error("HtmlTemplates", ex.Message, ex); }
        finally { Busy = false; }
    }

    /// <inheritdoc/>
    public override async Task ImportExcelAsync()
    {
        if (!_main.IsConnected) { Status = "Connect to an environment first."; return; }
        var plan = new ModelMeister.Ui.Services.Import.Plans.HtmlTemplatesImportPlan(_main, _shell, _log);
        var ran = await DialogHost.ShowImportWorkflowAsync(
            plan, _log, _main.Settings.Current.RecentWorkbookPaths).ConfigureAwait(true);
        if (!ran) return;
        RememberWorkbook(_main.Settings, plan.LastWorkbookPath);
        MarkDataDirty();
        await RefreshAsync().ConfigureAwait(true);
    }
}

/// <summary>Selectable grid row wrapping an <see cref="HtmlTemplateDto"/> for the templates list.</summary>
public sealed partial class HtmlTemplateRow : SelectableRow
{
    public HtmlTemplateRow(HtmlTemplateDto source) => Source = source;
    public HtmlTemplateDto Source { get; }
    public string Name => Source.Name;
    public string TemplateType => Source.TemplateType;
    public int ContentLength => Source.Content.Length;
    public string SizeLabel => $"{Source.Content.Length:n0}";
}
