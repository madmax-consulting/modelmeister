using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Model.Loading;
using ModelMeister.Model.Validation;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model for the Model page. Builds/loads a model project, surfaces concept counts and
/// validation issues, drives the issue-table filters, and tracks the recent-files MRU.
/// </summary>
public partial class ModelViewModel : ViewModelBase
{
    private const int MaxRecents = 10;

    private readonly MainWindowViewModel _main;
    private readonly ISettingsStore _settings;
    private readonly Shell _shell;
    private readonly IFileOpener _fileOpener;
    private readonly IAppLog _log;

    /// <summary>Unfiltered cache of validation issues; <see cref="Issues"/> is its filtered projection.</summary>
    private readonly List<ValidationRow> _allIssues = new();

    /// <summary>Path to the csproj or dll currently selected (not necessarily loaded yet).</summary>
    [ObservableProperty] private string? _modelPath;
    /// <summary>True while the model is being built/loaded.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    private bool _busy;
    /// <summary>Short status message at the bottom of the page.</summary>
    [ObservableProperty] private string _statusMessage = "Pick a model project to begin.";
    /// <summary>True once a model has loaded successfully (drives the empty-state vs results UI).</summary>
    [ObservableProperty] private bool _hasModel;
    /// <summary>Count of "Info" severity validation issues.</summary>
    [ObservableProperty] private int _infoCount;

    /// <summary>Top-level concept counts (entity types, fields, CVLs, ...). Displayed as pill chips.</summary>
    public ObservableCollection<ConceptCount> Counts { get; } = [];
    /// <summary>Validation issues after applying <see cref="SelectedCodeFilter"/> / <see cref="SelectedSeverityFilter"/>.</summary>
    public ObservableCollection<ValidationRow> Issues { get; } = [];
    /// <summary>Histogram of issue code &#8594; count, sorted descending by count.</summary>
    public ObservableCollection<CodeHistogramRow> CodeHistogram { get; } = [];
    /// <summary>MRU of model paths shown as quick-open chips.</summary>
    public ObservableCollection<string> RecentModelPaths { get; } = [];

    /// <summary>Field rows for the Fields concept table (rich, per-property columns).</summary>
    public ObservableCollection<FieldRow> FieldRows { get; } = [];
    /// <summary>CVL rows (CVL id, data type, value count) for the CVLs concept table.</summary>
    public ObservableCollection<ModelCvlRow> ModelCvlRows { get; } = [];
    /// <summary>Link-type rows (id, source, target) for the Link types concept table.</summary>
    public ObservableCollection<LinkTypeRow> LinkTypeRows { get; } = [];
    /// <summary>Role rows (name, permissions list) for the Roles concept table.</summary>
    public ObservableCollection<RoleRow> RoleRows { get; } = [];
    /// <summary>Values of the currently selected CVL — drives the expansion table under the CVLs grid.</summary>
    public ObservableCollection<CvlValueRow> SelectedCvlValues { get; } = [];

    /// <summary>The CVL row the user has clicked in the CVLs table; selecting one populates <see cref="SelectedCvlValues"/>.</summary>
    [ObservableProperty] private ModelCvlRow? _selectedModelCvlRow;
    partial void OnSelectedModelCvlRowChanged(ModelCvlRow? value)
    {
        SelectedCvlValues.Clear();
        if (value is null) return;
        foreach (var v in value.Cvl.Values)
            SelectedCvlValues.Add(new CvlValueRow(
                v.Key,
                v.Value?.ToString() ?? "",
                v.Parent ?? "",
                v.Index,
                v.Deactivated));
    }

    /// <summary>Count of "Error" severity validation issues.</summary>
    [ObservableProperty] private int _errorCount;
    /// <summary>Count of "Warning" severity validation issues.</summary>
    [ObservableProperty] private int _warningCount;
    /// <summary>Currently highlighted issue (drives the "Open source" command).</summary>
    [ObservableProperty] private ValidationRow? _selectedIssue;
    /// <summary>Active code filter (e.g. "MM024"); null = no code filter.</summary>
    [ObservableProperty] private string? _selectedCodeFilter;
    /// <summary>Active severity filter ("Error"/"Warning"/"Info"); null = no severity filter.</summary>
    [ObservableProperty] private string? _selectedSeverityFilter;
    /// <summary>The concept chip the user has clicked on; drives the items table shown below the chip strip.</summary>
    [ObservableProperty] private ConceptCount? _selectedConcept;

    public ModelViewModel(MainWindowViewModel main, ISettingsStore settings, Shell shell, IFileOpener fileOpener, IAppLog log)
    {
        _main = main;
        _settings = settings;
        _shell = shell;
        _fileOpener = fileOpener;
        _log = log;

        foreach (var path in settings.Current.RecentModelPaths.Where(File.Exists).Take(MaxRecents))
            RecentModelPaths.Add(path);

        if (!string.IsNullOrEmpty(settings.Current.LastModelPath) && File.Exists(settings.Current.LastModelPath))
            ModelPath = settings.Current.LastModelPath;
    }

    /// <summary>True when at least one of <see cref="SelectedCodeFilter"/> / <see cref="SelectedSeverityFilter"/> is set.
    /// Drives the visibility of the "Clear filters" button + its separator in the strip.</summary>
    public bool HasActiveFilter
        => !string.IsNullOrEmpty(SelectedCodeFilter) || !string.IsNullOrEmpty(SelectedSeverityFilter);

    /// <summary>True when a model is loaded and the user is NOT currently drilling into a concept —
    /// hides the Validation issues / filter strip while the concept table is open.</summary>
    public bool ShowIssuesSection => HasModel && SelectedConcept is null;

    partial void OnHasModelChanged(bool value) => OnPropertyChanged(nameof(ShowIssuesSection));
    partial void OnSelectedConceptChanged(ConceptCount? value) => OnPropertyChanged(nameof(ShowIssuesSection));

    partial void OnSelectedCodeFilterChanged(string? value)
    {
        RefreshFiltered();
        OnPropertyChanged(nameof(HasActiveFilter));
    }
    partial void OnSelectedSeverityFilterChanged(string? value)
    {
        RefreshFiltered();
        OnPropertyChanged(nameof(HasActiveFilter));
    }

    private bool CanLoadOrBrowse() => !Busy;

    [RelayCommand(CanExecute = nameof(CanLoadOrBrowse))]
    private async Task BrowseAsync()
    {
        var window = MainWindowOrNull();
        if (window is null) return;

        var picks = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select model csproj or dll",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Model project / assembly") { Patterns = new[] { "*.csproj", "*.dll" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        }).ConfigureAwait(true);

        if (picks.Count == 0) return;
        ModelPath = picks[0].TryGetLocalPath();
    }

    /// <summary>Build + load the model at <see cref="ModelPath"/> and run validation.</summary>
    [RelayCommand(CanExecute = nameof(CanLoadOrBrowse))]
    public async Task LoadAsync()
    {
        if (string.IsNullOrEmpty(ModelPath))
        {
            StatusMessage = "Choose a model path first.";
            return;
        }

        Busy = true;
        ResetState();

        try
        {
            StatusMessage = "Building & loading model…";
            _log.Info("Model", $"Loading model from {ModelPath}");

            // Force a hub-side invalidation up front: the user is about to load a (potentially)
            // different model, so any cached diff is now meaningless. Also covers the case where
            // LoadModelAsync returns an instance with the same reference identity, which would
            // otherwise skip OnLoadedModelChanged's invalidation branch.
            _main.InvalidateChangeSet(_main.ChangeSet is not null ? "Reloading model" : null);

            var model = await _shell.LoadModelAsync(ModelPath).ConfigureAwait(true);
            _main.LoadedModel = model;
            _main.ModelPath = ModelPath;
            UpsertRecent(ModelPath);
            _settings.Current.LastModelPath = ModelPath;
            _settings.Save();
            HasModel = true;

            PopulateCounts(model);

            var result = _shell.Validate(model);
            ErrorCount = result.Issues.Count(i => i.Severity == Severity.Error);
            WarningCount = result.Issues.Count(i => i.Severity == Severity.Warning);
            InfoCount = result.Issues.Count - ErrorCount - WarningCount;

            _allIssues.AddRange(result.Issues
                .OrderBy(x => x.Severity)
                .ThenBy(x => x.Code)
                .Select(i => new ValidationRow(i)));

            RefreshFiltered();
            BuildHistogram();

            StatusMessage = $"Model loaded — {ErrorCount} errors, {WarningCount} warnings, {InfoCount} info.";
            _log.Success("Model", $"Loaded {Path.GetFileName(ModelPath)}: {ErrorCount} errors, {WarningCount} warnings.");

            if (ErrorCount > 0)
                _log.Toast(LogLevel.Warn, "Validation errors found", $"{ErrorCount} errors · {WarningCount} warnings");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
            _log.Error("Model", $"Load failed: {ex.Message}");
            _log.Toast(LogLevel.Error, "Model load failed", ex.Message);
            HasModel = false;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void OpenIssue()
    {
        if (SelectedIssue?.Issue.Source is { } src) OpenSource(src);
    }

    [RelayCommand] private Task CopyIssueMessage() => Services.ClipboardHelpers.CopyAsync(SelectedIssue?.Message);
    [RelayCommand] private Task CopyIssueCode()    => Services.ClipboardHelpers.CopyAsync(SelectedIssue?.Code);
    [RelayCommand] private Task CopyIssueSource()  => Services.ClipboardHelpers.CopyAsync(SelectedIssue?.Source);

    /// <summary>Toggle the concept items table: re-clicking the active chip closes it.</summary>
    [RelayCommand]
    private void SelectConcept(ConceptCount? c)
        => SelectedConcept = ReferenceEquals(SelectedConcept, c) ? null : c;

    [RelayCommand]
    private void CloseConcept() => SelectedConcept = null;

    [RelayCommand]
    private void ClearFilters()
    {
        SelectedCodeFilter = null;
        SelectedSeverityFilter = null;
    }

    [RelayCommand]
    private void FilterByCode(string? code)
        => SelectedCodeFilter = string.IsNullOrEmpty(code) || SelectedCodeFilter == code ? null : code;

    [RelayCommand]
    private void FilterBySeverity(string? severity)
        => SelectedSeverityFilter = string.IsNullOrEmpty(severity) || SelectedSeverityFilter == severity ? null : severity;

    /// <summary>Open the source file referenced by a "file:line" string in the user's editor.</summary>
    public void OpenSource(string source)
    {
        var (file, line) = ParseSource(source);
        if (file is not null) _fileOpener.OpenAt(file, line);
    }

    private static (string? File, int Line) ParseSource(string s)
    {
        var idx = s.LastIndexOf(':');
        // Drive letters look like "C:..." — only treat the colon as a line separator if it's past the drive prefix.
        if (idx <= 2) return (s, 1);
        var file = s[..idx];
        return int.TryParse(s.AsSpan(idx + 1), out var line) ? (file, line) : (s, 1);
    }

    private void ResetState()
    {
        _allIssues.Clear();
        Issues.Clear();
        CodeHistogram.Clear();
        Counts.Clear();
        FieldRows.Clear();
        ModelCvlRows.Clear();
        LinkTypeRows.Clear();
        RoleRows.Clear();
        SelectedCvlValues.Clear();
        SelectedModelCvlRow = null;
        SelectedCodeFilter = null;
        SelectedSeverityFilter = null;
    }

    private void PopulateCounts(LoadedModel m)
    {
        foreach (var e in m.EntityTypes)
            foreach (var f in e.Fields)
                FieldRows.Add(new FieldRow(
                    EntityTypeId: e.EntityTypeId,
                    FieldId: f.PropertyName,
                    DataType: f.DataType.ToString(),
                    Mandatory: f.Field.Mandatory,
                    ReadOnly: f.Field.ReadOnly,
                    MultiValue: f.Field.MultiValue,
                    Unique: f.Field.Unique,
                    Hidden: f.Field.Hidden,
                    TrackChanges: f.Field.TrackChanges,
                    ExcludeFromDefaultView: f.Field.ExcludeFromDefaultView,
                    Index: f.Field.Index,
                    CvlId: f.Field.Cvl?.Name ?? "",
                    Category: f.Field.Category?.Name ?? "",
                    DefaultValue: f.Field.DefaultValue?.ToString() ?? ""));

        foreach (var c in m.Cvls)
            ModelCvlRows.Add(new ModelCvlRow(c, c.CvlId, c.DataType.ToString(), c.Values.Count, c.ParentCvlId ?? "", c.CustomValueList));

        foreach (var l in m.LinkTypes)
            LinkTypeRows.Add(new LinkTypeRow(l.LinkTypeId, l.SourceEntityTypeId, l.TargetEntityTypeId, l.LinkEntityTypeId ?? "", l.Index));

        foreach (var r in m.Roles)
            RoleRows.Add(new RoleRow(r.Name, string.Join(", ", r.PermissionNames), r.PermissionNames.Count));

        ConceptCount[] counts =
        {
            new("EntityTypes", "Entity types", "Entity type", "Fields", m.EntityTypes
                .Select(e => new ConceptItem(e.EntityTypeId, e.Fields.Count.ToString()))
                .ToList()),
            new("Fields", "Fields", "Field", "Datatype", m.EntityTypes
                .SelectMany(e => e.Fields.Select(f => new ConceptItem($"{e.EntityTypeId}.{f.PropertyName}", f.DataType.ToString())))
                .ToList()),
            new("Cvls", "CVLs", "CVL", "Values", m.Cvls
                .Select(c => new ConceptItem(c.CvlId, $"{c.DataType} · {c.Values.Count}"))
                .ToList()),
            new("Categories", "Categories", "Category", "Index", m.Categories
                .Select(c => new ConceptItem(c.CategoryId, c.IsReserved ? "reserved" : c.Index.ToString()))
                .ToList()),
            new("Fieldsets", "Fieldsets", "Fieldset", "Entity type", m.Fieldsets
                .Select(f => new ConceptItem(f.FieldsetId, f.EntityTypeId))
                .ToList()),
            new("LinkTypes", "Link types", "Link type", "Source → Target", m.LinkTypes
                .Select(l => new ConceptItem(l.LinkTypeId, $"{l.SourceEntityTypeId} → {l.TargetEntityTypeId}"))
                .ToList()),
            new("Roles", "Roles", "Role", "Permissions", m.Roles
                .Select(r => new ConceptItem(r.Name, r.PermissionNames.Count.ToString()))
                .ToList()),
            new("Completeness", "Completeness groups", "Group", "Weight", m.CompletenessGroups
                .Select(g => new ConceptItem(g.ClrType.Name, g.Weight.ToString()))
                .ToList()),
            new("Languages", "Languages", "ISO code", "", m.Languages
                .Select(l => new ConceptItem(l.IsoCode, l.IsDefault ? "default" : ""))
                .ToList()),
        };
        foreach (var c in counts) Counts.Add(c);
        SelectedConcept = null;
    }

    private void BuildHistogram()
    {
        CodeHistogram.Clear();
        var rows = _allIssues
            .GroupBy(i => i.Code)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => new CodeHistogramRow(g.Key, g.Count(), g.First().Severity));
        foreach (var row in rows) CodeHistogram.Add(row);
    }

    private void RefreshFiltered()
    {
        Issues.Clear();

        var filtered = _allIssues
            .Where(r => string.IsNullOrEmpty(SelectedCodeFilter) || r.Code == SelectedCodeFilter)
            .Where(r => string.IsNullOrEmpty(SelectedSeverityFilter) || r.Severity == SelectedSeverityFilter);

        foreach (var row in filtered) Issues.Add(row);
    }

    private void UpsertRecent(string path)
    {
        var existing = RecentModelPaths.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) RecentModelPaths.Remove(existing);
        RecentModelPaths.Insert(0, path);
        while (RecentModelPaths.Count > MaxRecents) RecentModelPaths.RemoveAt(RecentModelPaths.Count - 1);
        _settings.Current.RecentModelPaths = RecentModelPaths.ToList();
    }

    /// <summary>Set the model path from a drag-and-drop interaction. Caller still has to invoke Load.</summary>
    public void AcceptDroppedPath(string path) => ModelPath = path;

    private static Window? MainWindowOrNull()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
            ? d.MainWindow
            : null;
}

/// <summary>One pill chip in the concept-counts row. <see cref="Kind"/> is a stable string discriminator
/// driving which concept-specific DataGrid renders ("Fields", "Cvls", "LinkTypes", "Roles" get rich tables;
/// the rest fall back to the generic two-column <see cref="Items"/> grid).</summary>
public sealed class ConceptCount
{
    public ConceptCount(string kind, string name, string primaryHeader, string secondaryHeader, IReadOnlyList<ConceptItem> items)
    {
        Kind = kind;
        Name = name;
        PrimaryHeader = primaryHeader;
        SecondaryHeader = secondaryHeader;
        Items = items;
    }
    public string Kind { get; }
    public string Name { get; }
    public string PrimaryHeader { get; }
    public string SecondaryHeader { get; }
    public IReadOnlyList<ConceptItem> Items { get; }
    public int Count => Items.Count;
}

/// <summary>One row in a <see cref="ConceptCount"/>'s items table. Headers come from the owning <see cref="ConceptCount"/>.</summary>
public sealed record ConceptItem(string Primary, string Secondary);

/// <summary>One field row in the Fields concept table — all columns the user asked for.</summary>
public sealed record FieldRow(
    string EntityTypeId,
    string FieldId,
    string DataType,
    bool Mandatory,
    bool ReadOnly,
    bool MultiValue,
    bool Unique,
    bool Hidden,
    bool? TrackChanges,
    bool? ExcludeFromDefaultView,
    int? Index,
    string CvlId,
    string Category,
    string DefaultValue);

/// <summary>One CVL row. Carries the <see cref="LoadedCvl"/> so selecting the row can expand its values below.</summary>
public sealed record ModelCvlRow(LoadedCvl Cvl, string CvlId, string DataType, int ValueCount, string ParentCvlId, bool CustomValueList);

/// <summary>One CVL value row in the expansion table under the CVLs grid.</summary>
public sealed record CvlValueRow(string Key, string Value, string Parent, int Index, bool Deactivated);

/// <summary>One link-type row with source/target as separate columns.</summary>
public sealed record LinkTypeRow(string LinkTypeId, string Source, string Target, string LinkEntity, int Index);

/// <summary>One role row carrying the permissions list as a comma-joined string + count for sorting.</summary>
public sealed record RoleRow(string Name, string Permissions, int PermissionCount);

/// <summary>Display projection of a <see cref="ValidationIssue"/> with string-typed columns for grid binding.</summary>
public sealed class ValidationRow
{
    public ValidationRow(ValidationIssue issue) { Issue = issue; }
    public ValidationIssue Issue { get; }
    public string Severity => Issue.Severity.ToString();
    public string Code => Issue.Code;
    public string Message => Issue.Message;
    public string Source => Issue.Source ?? "";
}

/// <summary>One bar in the code-histogram chart.</summary>
public sealed record CodeHistogramRow(string Code, int Count, string Severity)
{
    public string Header => $"{Code} ×{Count}";
}
