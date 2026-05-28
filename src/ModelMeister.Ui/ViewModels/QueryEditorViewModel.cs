using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.WorkAreas;
using ModelMeister.Inriver.WorkAreas.Query;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Backs the GUI query builder dialog. Loads a folder's saved search into the editable
/// <see cref="QueryModel"/>, presents Data / System / Link criteria as row collections, validates field /
/// entity / link references against the connected env live, and produces the serialized query JSON on Save.
/// Completeness / specification sub-queries the builder can't edit are preserved verbatim (see
/// <see cref="QueryMapper.ToComplexQuery"/>); the user is warned when a query carries them.
/// </summary>
public partial class QueryEditorViewModel : ViewModelBase
{
    private readonly QueryMetadata _meta;
    private readonly inRiver.Remoting.Query.ComplexQuery? _original;
    private readonly bool _hadUnsupportedParts;

    public QueryEditorViewModel(string folderName, string? existingQueryJson, QueryMetadata meta)
    {
        FolderName = folderName;
        _meta = meta;
        _original = WorkAreaService.DeserializeQuery(existingQueryJson);
        var model = QueryMapper.ToModel(_original);
        _hadUnsupportedParts = model.HasUnsupportedParts;

        _entityTypeId = model.EntityTypeId ?? "";
        _channelText = model.ChannelId?.ToString() ?? "";
        _dataJoin = model.DataQuery?.Join ?? QJoin.And;
        foreach (var c in model.DataQuery?.Criteria ?? []) DataCriteria.Add(Track(new CriterionRowViewModel(c)));
        foreach (var s in model.SystemCriteria) SystemCriteria.Add(Track(new SystemRowViewModel(s)));

        if (model.LinkQuery is { } lq)
        {
            IncludeLink = true;
            _linkTypeId = lq.LinkTypeId ?? "";
            _linkDirection = lq.Direction;
            _linkSourceEntityTypeId = lq.SourceEntityTypeId ?? "";
            _linkTargetEntityTypeId = lq.TargetEntityTypeId ?? "";
            foreach (var c in lq.SourceCriteria) LinkSourceCriteria.Add(Track(new CriterionRowViewModel(c)));
            foreach (var c in lq.TargetCriteria) LinkTargetCriteria.Add(Track(new CriterionRowViewModel(c)));
        }

        DataCriteria.CollectionChanged += OnCollectionChanged;
        SystemCriteria.CollectionChanged += OnCollectionChanged;
        LinkSourceCriteria.CollectionChanged += OnCollectionChanged;
        LinkTargetCriteria.CollectionChanged += OnCollectionChanged;
        Revalidate();
    }

    public string FolderName { get; }
    public string Title => $"Edit query · {FolderName}";

    // ----- pick lists -----
    public IReadOnlyList<QOperator> Operators { get; } = Enum.GetValues<QOperator>();
    public IReadOnlyList<QJoin> Joins { get; } = Enum.GetValues<QJoin>();
    public IReadOnlyList<QLinkDirection> Directions { get; } = Enum.GetValues<QLinkDirection>();
    public IReadOnlyList<SystemField> SystemFields { get; } = Enum.GetValues<SystemField>();
    public IReadOnlyList<string> AvailableEntityTypes => _meta.EntityTypeIds;
    public IReadOnlyList<string> AvailableFields => _meta.AllFieldTypeIds;
    public IReadOnlyList<string> AvailableLinkTypes => _meta.LinkTypeIds;

    // ----- top-level -----
    [ObservableProperty] private string _entityTypeId;
    [ObservableProperty] private string _channelText;
    [ObservableProperty] private QJoin _dataJoin;

    public ObservableCollection<CriterionRowViewModel> DataCriteria { get; } = [];
    public ObservableCollection<SystemRowViewModel> SystemCriteria { get; } = [];

    // ----- link -----
    [ObservableProperty] private bool _includeLink;
    [ObservableProperty] private string _linkTypeId = "";
    [ObservableProperty] private QLinkDirection _linkDirection;
    [ObservableProperty] private string _linkSourceEntityTypeId = "";
    [ObservableProperty] private string _linkTargetEntityTypeId = "";
    public ObservableCollection<CriterionRowViewModel> LinkSourceCriteria { get; } = [];
    public ObservableCollection<CriterionRowViewModel> LinkTargetCriteria { get; } = [];

    // ----- validation / warnings -----
    [ObservableProperty] private string _validation = "";
    public bool HasWarnings => !string.IsNullOrEmpty(Validation);

    partial void OnEntityTypeIdChanged(string value) => Revalidate();
    partial void OnIncludeLinkChanged(bool value) => Revalidate();
    partial void OnLinkTypeIdChanged(string value) => Revalidate();
    partial void OnLinkSourceEntityTypeIdChanged(string value) => Revalidate();
    partial void OnLinkTargetEntityTypeIdChanged(string value) => Revalidate();

    // ----- commands -----
    [RelayCommand] private void AddDataCriterion() => DataCriteria.Add(Track(new CriterionRowViewModel()));
    [RelayCommand] private void AddSystemCriterion() => SystemCriteria.Add(Track(new SystemRowViewModel()));
    [RelayCommand] private void AddLinkSourceCriterion() => LinkSourceCriteria.Add(Track(new CriterionRowViewModel()));
    [RelayCommand] private void AddLinkTargetCriterion() => LinkTargetCriteria.Add(Track(new CriterionRowViewModel()));

    [RelayCommand] private void RemoveData(CriterionRowViewModel? row) { if (row is not null) DataCriteria.Remove(row); }
    [RelayCommand] private void RemoveSystem(SystemRowViewModel? row) { if (row is not null) SystemCriteria.Remove(row); }
    [RelayCommand] private void RemoveLinkSource(CriterionRowViewModel? row) { if (row is not null) LinkSourceCriteria.Remove(row); }
    [RelayCommand] private void RemoveLinkTarget(CriterionRowViewModel? row) { if (row is not null) LinkTargetCriteria.Remove(row); }

    // ----- build / result -----

    /// <summary>Project the editor state back into a <see cref="QueryModel"/>.</summary>
    public QueryModel BuildModel()
    {
        var model = new QueryModel
        {
            EntityTypeId = string.IsNullOrWhiteSpace(EntityTypeId) ? null : EntityTypeId.Trim(),
            ChannelId = int.TryParse(ChannelText, out var ch) ? ch : null,
            DataQuery = DataCriteria.Count == 0
                ? null
                : new CriteriaGroup { Join = DataJoin, Criteria = DataCriteria.Select(r => r.ToModel()).ToList() },
            SystemCriteria = SystemCriteria.Select(r => r.ToModel()).ToList(),
            HasUnsupportedParts = _hadUnsupportedParts,
        };
        if (IncludeLink)
            model.LinkQuery = new LinkQueryModel
            {
                LinkTypeId = string.IsNullOrWhiteSpace(LinkTypeId) ? null : LinkTypeId.Trim(),
                Direction = LinkDirection,
                SourceEntityTypeId = string.IsNullOrWhiteSpace(LinkSourceEntityTypeId) ? null : LinkSourceEntityTypeId.Trim(),
                TargetEntityTypeId = string.IsNullOrWhiteSpace(LinkTargetEntityTypeId) ? null : LinkTargetEntityTypeId.Trim(),
                SourceCriteria = LinkSourceCriteria.Select(r => r.ToModel()).ToList(),
                TargetCriteria = LinkTargetCriteria.Select(r => r.ToModel()).ToList(),
            };
        return model;
    }

    /// <summary>The serialized query JSON to store on the folder (null when the query is empty).</summary>
    public string? ResultJson { get; private set; }

    public bool? Result { get; private set; }
    public event Action? Closed;

    [RelayCommand]
    private void Confirm()
    {
        var model = BuildModel();
        ResultJson = WorkAreaService.SerializeQuery(QueryMapper.ToComplexQuery(model, _original));
        Result = true;
        Closed?.Invoke();
    }

    [RelayCommand]
    private void Abort()
    {
        Result = false;
        Closed?.Invoke();
    }

    // ----- internals -----

    private T Track<T>(T row) where T : INotifyPropertyChanged
    {
        row.PropertyChanged += (_, _) => Revalidate();
        return row;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Revalidate();

    private void Revalidate()
    {
        var warnings = QueryValidator.Validate(BuildModel(), _meta).ToList();
        if (_hadUnsupportedParts)
            warnings.Insert(0, "This query has completeness/specification criteria the builder can't show — they are preserved unchanged when you save.");
        Validation = warnings.Count == 0 ? "" : string.Join(Environment.NewLine, warnings);
        OnPropertyChanged(nameof(HasWarnings));
    }
}

/// <summary>One editable field criterion row (Data or Link source/target).</summary>
public partial class CriterionRowViewModel : ObservableObject
{
    public CriterionRowViewModel() { }

    public CriterionRowViewModel(CriterionModel c)
    {
        _fieldTypeId = c.FieldTypeId;
        _operator = c.Operator;
        _value = c.Value ?? "";
    }

    [ObservableProperty] private string _fieldTypeId = "";
    [ObservableProperty] private QOperator _operator = QOperator.Equal;
    [ObservableProperty] private string _value = "";

    public CriterionModel ToModel() => new()
    {
        FieldTypeId = FieldTypeId?.Trim() ?? "",
        Operator = Operator,
        Value = string.IsNullOrEmpty(Value) ? null : Value,
    };
}

/// <summary>One editable system-field criterion row.</summary>
public partial class SystemRowViewModel : ObservableObject
{
    public SystemRowViewModel() { }

    public SystemRowViewModel(SystemFieldCriterion c)
    {
        _field = c.Field;
        _operator = c.Operator;
        _value = c.Value ?? "";
    }

    [ObservableProperty] private SystemField _field = SystemField.CreatedBy;
    [ObservableProperty] private QOperator _operator = QOperator.Equal;
    [ObservableProperty] private string _value = "";

    public SystemFieldCriterion ToModel() => new()
    {
        Field = Field,
        Operator = Operator,
        Value = string.IsNullOrEmpty(Value) ? null : Value,
    };
}
