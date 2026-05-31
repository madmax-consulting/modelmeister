using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Inriver.WorkAreas;
using ModelMeister.Inriver.WorkAreas.Query;

namespace ModelMeister.Ui.ViewModels;

/// <summary>How the value editor for a criterion row is presented, derived from the field's inriver data type.</summary>
public enum ValueKind { Text, Bool, Date, Number, Cvl }

/// <summary>
/// Backs the GUI query builder dialog. Loads a folder's saved search into an editable, recursive group tree
/// (<see cref="DataRoot"/>), presents System and Link criteria as row collections, offers typed value editors
/// per field data type, a raw-JSON view/edit toggle and a live human-readable summary, validates field /
/// entity / link references plus structural/datatype mistakes against the connected env live, and produces the
/// serialized query JSON on Save.
/// <para>Completeness / specification sub-queries the builder can't edit are preserved verbatim (see
/// <see cref="QueryMapper.ToComplexQuery"/>); the user is warned when a query carries them. The typed model is
/// an editing projection only — the canonical wire format stays the serialized <see cref="QueryModel"/> JSON,
/// which round-trips byte-stably through <see cref="WorkAreaService.SerializeQuery"/>.</para>
/// </summary>
public partial class QueryEditorViewModel : ViewModelBase
{
    private readonly QueryMetadata _meta;
    private readonly inRiver.Remoting.Query.ComplexQuery? _original;
    private readonly bool _hadUnsupportedParts;
    private readonly Func<inRiver.Remoting.Query.ComplexQuery, Task<int>>? _preview;
    private bool _suspendValidation;

    public QueryEditorViewModel(string folderName, string? existingQueryJson, QueryMetadata meta)
        : this(folderName, existingQueryJson, meta, null) { }

    /// <summary>Test/host ctor: <paramref name="preview"/> (when non-null) supplies a live match-count for the
    /// Preview command without coupling the view-model to a connection.</summary>
    public QueryEditorViewModel(
        string folderName, string? existingQueryJson, QueryMetadata meta,
        Func<inRiver.Remoting.Query.ComplexQuery, Task<int>>? preview)
    {
        FolderName = folderName;
        _meta = meta;
        _preview = preview;
        _original = WorkAreaService.DeserializeQuery(existingQueryJson);
        var model = QueryMapper.ToModel(_original);
        _hadUnsupportedParts = model.HasUnsupportedParts;

        _suspendValidation = true;
        _entityTypeId = model.EntityTypeId ?? "";
        _channelText = model.ChannelId?.ToString() ?? "";

        DataRoot = BuildGroup(model.DataQuery ?? new CriteriaGroup { Join = QJoin.And });
        foreach (var s in model.SystemCriteria) SystemCriteria.Add(Track(new SystemRowViewModel(s)));

        if (model.LinkQuery is { } lq)
        {
            _includeLink = true;
            _linkTypeId = lq.LinkTypeId ?? "";
            _linkDirection = lq.Direction;
            _linkSourceEntityTypeId = lq.SourceEntityTypeId ?? "";
            _linkTargetEntityTypeId = lq.TargetEntityTypeId ?? "";
            foreach (var c in lq.SourceCriteria) LinkSourceCriteria.Add(Track(NewRow(c)));
            foreach (var c in lq.TargetCriteria) LinkTargetCriteria.Add(Track(NewRow(c)));
        }

        SystemCriteria.CollectionChanged += OnCollectionChanged;
        LinkSourceCriteria.CollectionChanged += OnCollectionChanged;
        LinkTargetCriteria.CollectionChanged += OnCollectionChanged;
        _suspendValidation = false;
        Recompute();
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

    /// <summary>Field ids scoped to the chosen entity type (falls back to all fields when none chosen).</summary>
    public IReadOnlyList<string> ScopedFields => _meta.FieldsFor(EntityTypeId);

    // ----- top-level -----
    [ObservableProperty] private string _entityTypeId;
    [ObservableProperty] private string _channelText;

    /// <summary>The recursive root group of the data query (criteria + nested sub-groups).</summary>
    public GroupRowViewModel DataRoot { get; }

    public ObservableCollection<SystemRowViewModel> SystemCriteria { get; } = [];

    // ----- link -----
    [ObservableProperty] private bool _includeLink;
    [ObservableProperty] private string _linkTypeId = "";
    [ObservableProperty] private QLinkDirection _linkDirection;
    [ObservableProperty] private string _linkSourceEntityTypeId = "";
    [ObservableProperty] private string _linkTargetEntityTypeId = "";
    public ObservableCollection<CriterionRowViewModel> LinkSourceCriteria { get; } = [];
    public ObservableCollection<CriterionRowViewModel> LinkTargetCriteria { get; } = [];

    // ----- validation / summary / raw -----
    [ObservableProperty] private string _validation = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _showRawJson;
    [ObservableProperty] private string _rawJson = "";
    [ObservableProperty] private string? _rawJsonError;
    [ObservableProperty] private string? _previewResult;
    public bool HasWarnings => !string.IsNullOrEmpty(Validation);
    public bool CanPreview => _preview is not null;

    partial void OnEntityTypeIdChanged(string value) { OnPropertyChanged(nameof(ScopedFields)); Recompute(); }
    partial void OnChannelTextChanged(string value) => Recompute();
    partial void OnIncludeLinkChanged(bool value) => Recompute();
    partial void OnLinkTypeIdChanged(string value) => Recompute();
    partial void OnLinkDirectionChanged(QLinkDirection value) => Recompute();
    partial void OnLinkSourceEntityTypeIdChanged(string value) => Recompute();
    partial void OnLinkTargetEntityTypeIdChanged(string value) => Recompute();

    partial void OnShowRawJsonChanged(bool value)
    {
        if (value)
        {
            // Entering raw view: serialize the current built query so the user edits the canonical form.
            RawJsonError = null;
            RawJson = WorkAreaService.SerializeQuery(QueryMapper.ToComplexQuery(BuildModel(), _original)) ?? "";
        }
        else
        {
            // Leaving raw view: try to parse edits back into rows; keep raw view on failure.
            if (!TryApplyRawJson()) _showRawJson = true; // revert without re-triggering the partial
        }
    }

    // ----- commands -----
    [RelayCommand] private void AddSystemCriterion() => SystemCriteria.Add(Track(new SystemRowViewModel()));
    [RelayCommand] private void AddLinkSourceCriterion() => LinkSourceCriteria.Add(Track(NewRow()));
    [RelayCommand] private void AddLinkTargetCriterion() => LinkTargetCriteria.Add(Track(NewRow()));

    [RelayCommand] private void RemoveSystem(SystemRowViewModel? row) { if (row is not null) SystemCriteria.Remove(row); }
    [RelayCommand] private void RemoveLinkSource(CriterionRowViewModel? row) { if (row is not null) LinkSourceCriteria.Remove(row); }
    [RelayCommand] private void RemoveLinkTarget(CriterionRowViewModel? row) { if (row is not null) LinkTargetCriteria.Remove(row); }

    [RelayCommand]
    private async Task Preview()
    {
        if (_preview is null) return;
        try
        {
            var count = await _preview(QueryMapper.ToComplexQuery(BuildModel(), _original)).ConfigureAwait(true);
            PreviewResult = $"{count:N0} matching entit{(count == 1 ? "y" : "ies")}";
        }
        catch (Exception ex)
        {
            PreviewResult = $"Preview failed: {ex.Message}";
        }
    }

    // ----- build / result -----

    /// <summary>Project the editor state back into a <see cref="QueryModel"/>. Emits the full nested group tree
    /// (data query) so an existing <see cref="CriteriaGroup.SubQuery"/> is never silently dropped on save.</summary>
    public QueryModel BuildModel()
    {
        var model = new QueryModel
        {
            EntityTypeId = string.IsNullOrWhiteSpace(EntityTypeId) ? null : EntityTypeId.Trim(),
            ChannelId = int.TryParse(ChannelText, out var ch) ? ch : null,
            DataQuery = DataRoot.ToModel(),
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
        // If the user is in raw mode with unparseable edits, block save.
        if (ShowRawJson && !TryApplyRawJson()) return;
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

    private CriterionRowViewModel NewRow(CriterionModel? c = null)
    {
        var row = c is null ? new CriterionRowViewModel() : new CriterionRowViewModel(c);
        row.AttachMetadata(_meta);
        return row;
    }

    private GroupRowViewModel BuildGroup(CriteriaGroup group)
    {
        var vm = new GroupRowViewModel(group.Join);
        foreach (var c in group.Criteria) vm.Criteria.Add(NewRow(c));
        // Single nested SubQuery level in v1 (matches inriver's capability); deeper chains fold left.
        if (group.SubQuery is not null) vm.SubGroups.Add(BuildGroup(group.SubQuery));
        vm.Bind(this, NewRow);
        return vm;
    }

    private T Track<T>(T row) where T : INotifyPropertyChanged
    {
        row.PropertyChanged += (_, _) => Recompute();
        return row;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (var item in e.NewItems.OfType<INotifyPropertyChanged>())
                item.PropertyChanged += (_, _) => Recompute();
        Recompute();
    }

    /// <summary>Called by the recursive group tree whenever any descendant changes.</summary>
    internal void NotifyChanged() => Recompute();

    private bool TryApplyRawJson()
    {
        var parsed = WorkAreaService.DeserializeQuery(RawJson);
        if (parsed is null && !string.IsNullOrWhiteSpace(RawJson))
        {
            RawJsonError = "The JSON could not be parsed as a saved query.";
            return false;
        }
        RawJsonError = null;
        var model = QueryMapper.ToModel(parsed);

        _suspendValidation = true;
        EntityTypeId = model.EntityTypeId ?? "";
        ChannelText = model.ChannelId?.ToString() ?? "";
        DataRoot.Load(model.DataQuery ?? new CriteriaGroup { Join = QJoin.And }, NewRow, BuildGroup);

        SystemCriteria.Clear();
        foreach (var s in model.SystemCriteria) SystemCriteria.Add(Track(new SystemRowViewModel(s)));

        LinkSourceCriteria.Clear();
        LinkTargetCriteria.Clear();
        IncludeLink = model.LinkQuery is not null;
        if (model.LinkQuery is { } lq)
        {
            LinkTypeId = lq.LinkTypeId ?? "";
            LinkDirection = lq.Direction;
            LinkSourceEntityTypeId = lq.SourceEntityTypeId ?? "";
            LinkTargetEntityTypeId = lq.TargetEntityTypeId ?? "";
            foreach (var c in lq.SourceCriteria) LinkSourceCriteria.Add(Track(NewRow(c)));
            foreach (var c in lq.TargetCriteria) LinkTargetCriteria.Add(Track(NewRow(c)));
        }
        _suspendValidation = false;
        Recompute();
        return true;
    }

    private void Recompute()
    {
        if (_suspendValidation) return;
        var model = BuildModel();

        var warnings = QueryValidator.Validate(model, _meta).ToList();
        if (_hadUnsupportedParts)
            warnings.Insert(0, "This query has completeness/specification criteria the builder can't show — they are preserved unchanged when you save.");
        Validation = warnings.Count == 0 ? "" : string.Join(Environment.NewLine, warnings);
        OnPropertyChanged(nameof(HasWarnings));

        Summary = QuerySummary.Describe(model);
        PreviewResult = null;
    }
}

/// <summary>
/// A recursive criteria group: a join (AND/OR), a flat list of field criteria, and nested sub-groups. The UI
/// tree is n-ary for a clean UX; the mapper folds it into inriver's single <see cref="CriteriaGroup.SubQuery"/>
/// chain (v1 supports one nested level — deeper UI groups left-nest and the validator flags the rest).
/// </summary>
public partial class GroupRowViewModel : ObservableObject
{
    private QueryEditorViewModel? _owner;
    private Func<CriterionModel?, CriterionRowViewModel>? _rowFactory;

    public GroupRowViewModel() { }
    public GroupRowViewModel(QJoin join) => _join = join;

    [ObservableProperty] private QJoin _join;

    public ObservableCollection<CriterionRowViewModel> Criteria { get; } = [];
    public ObservableCollection<GroupRowViewModel> SubGroups { get; } = [];

    /// <summary>True for nested groups (shows a remove-group affordance); false for the editor's root.</summary>
    [ObservableProperty] private bool _isRemovable;

    partial void OnJoinChanged(QJoin value) => _owner?.NotifyChanged();

    internal void Bind(QueryEditorViewModel owner, Func<CriterionModel?, CriterionRowViewModel> rowFactory)
    {
        _owner = owner;
        _rowFactory = rowFactory;
        Criteria.CollectionChanged += OnChildChanged;
        SubGroups.CollectionChanged += OnChildChanged;
        foreach (var c in Criteria) c.PropertyChanged += OnLeafChanged;
        foreach (var g in SubGroups) { g.IsRemovable = true; g.Bind(owner, rowFactory); }
    }

    private void OnChildChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (var item in e.NewItems)
            {
                if (item is CriterionRowViewModel row) row.PropertyChanged += OnLeafChanged;
                else if (item is GroupRowViewModel g && _owner is not null && _rowFactory is not null)
                { g.IsRemovable = true; g.Bind(_owner, _rowFactory); }
            }
        _owner?.NotifyChanged();
    }

    private void OnLeafChanged(object? sender, PropertyChangedEventArgs e) => _owner?.NotifyChanged();

    [RelayCommand]
    private void AddCriterion()
    {
        if (_rowFactory is not null) Criteria.Add(_rowFactory(null));
    }

    [RelayCommand]
    private void AddGroup()
    {
        if (_owner is null || _rowFactory is null) return;
        SubGroups.Add(new GroupRowViewModel(QJoin.And));
    }

    [RelayCommand]
    private void RemoveCriterion(CriterionRowViewModel? row) { if (row is not null) Criteria.Remove(row); }

    [RelayCommand]
    private void RemoveGroup(GroupRowViewModel? group) { if (group is not null) SubGroups.Remove(group); }

    /// <summary>Replace this group's contents from a model (used by the raw-JSON apply path).</summary>
    internal void Load(
        CriteriaGroup group,
        Func<CriterionModel?, CriterionRowViewModel> rowFactory,
        Func<CriteriaGroup, GroupRowViewModel> groupFactory)
    {
        Join = group.Join;
        Criteria.Clear();
        SubGroups.Clear();
        foreach (var c in group.Criteria) Criteria.Add(rowFactory(c));
        if (group.SubQuery is not null) SubGroups.Add(groupFactory(group.SubQuery));
    }

    /// <summary>Fold the n-ary UI tree into inriver's single-SubQuery chain. The first sub-group becomes this
    /// group's <see cref="CriteriaGroup.SubQuery"/>; any further sub-groups left-nest beneath it (v1: only one
    /// nested level survives inriver's shape — the validator surfaces the limitation rather than dropping rows).</summary>
    public CriteriaGroup? ToModel()
    {
        var group = new CriteriaGroup
        {
            Join = Join,
            Criteria = Criteria.Select(r => r.ToModel()).ToList(),
        };

        CriteriaGroup? sub = null;
        // Fold sub-groups right-to-left so the first listed sub-group ends up outermost.
        foreach (var child in SubGroups.Reverse())
        {
            var childModel = child.ToModel();
            if (childModel is null) continue;
            if (sub is null) sub = childModel;
            else { childModel.SubQuery = sub; sub = childModel; }
        }
        group.SubQuery = sub;

        // An entirely empty group (no criteria, no sub) maps to null so byte-stable round-trip holds.
        return group.Criteria.Count == 0 && group.SubQuery is null ? null : group;
    }
}

/// <summary>One editable field criterion row (Data, nested group, or Link source/target).</summary>
public partial class CriterionRowViewModel : ObservableObject
{
    private QueryMetadata? _meta;

    public CriterionRowViewModel() { }

    // Preserve the source's nullable interval so an untouched row round-trips byte-stably (inriver writes
    // an explicit false; collapsing to null would phantom-diff). The checkbox sets it explicitly.
    private bool? _intervalOriginal;

    public CriterionRowViewModel(CriterionModel c)
    {
        _fieldTypeId = c.FieldTypeId;
        _operator = c.Operator;
        _value = c.Value ?? "";
        _intervalOriginal = c.Interval;
        _interval = c.Interval ?? false;
        _language = c.Language ?? "";
    }

    [ObservableProperty] private string _fieldTypeId = "";
    [ObservableProperty] private QOperator _operator = QOperator.Equal;
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private bool _interval;
    [ObservableProperty] private string _language = "";

    partial void OnIntervalChanged(bool value) => _intervalOriginal = value;

    /// <summary>Drives which value editor the view shows (typed by the field's inriver data type).</summary>
    [ObservableProperty] private ValueKind _valueKind = ValueKind.Text;

    /// <summary>Two-way bridge for the bool value editor (mirrors <see cref="Value"/>'s "true"/"false").</summary>
    public bool BoolValue
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }

    /// <summary>Two-way bridge for the number value editor.</summary>
    public decimal? NumberValue
    {
        get => decimal.TryParse(Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
        set => Value = value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    /// <summary>Two-way bridge for the date value editor (round-trips ISO-8601).</summary>
    public DateTimeOffset? DateValue
    {
        get => DateTimeOffset.TryParse(Value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        set => Value = value?.ToString("o", System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(NumberValue));
        OnPropertyChanged(nameof(DateValue));
        OnPropertyChanged(nameof(CvlValueText));
        OnPropertyChanged(nameof(ResolvedCvlDisplay));
    }

    partial void OnFieldTypeIdChanged(string value) => ResolveValueKind();

    internal void AttachMetadata(QueryMetadata meta) { _meta = meta; ResolveValueKind(); }

    /// <summary>The CVL values this field can take, for the model-driven value dropdown. Empty unless the
    /// field is a CVL field with captured values.</summary>
    public IReadOnlyList<CvlValueOption> CvlOptions { get; private set; } = [];

    /// <summary>Autocomplete suggestions for the CVL value box — each is "Display · Key" so the user can match
    /// on either the friendly name or the stored key.</summary>
    public IReadOnlyList<string> CvlSearchStrings { get; private set; } = [];

    /// <summary>True when the value should be picked from the model-driven CVL dropdown (a CVL field that has
    /// captured values). When false the row falls back to a free-text box (incl. CVL fields with no values).</summary>
    public bool UseCvlPicker => ValueKind == ValueKind.Cvl && CvlOptions.Count > 0;

    /// <summary>True when the plain free-text editor should show: a text field, or a CVL field whose values
    /// couldn't be read (so the admin can still type a key by hand).</summary>
    public bool UseTextEditor => ValueKind == ValueKind.Text || (ValueKind == ValueKind.Cvl && CvlOptions.Count == 0);

    /// <summary>Two-way bridge for the CVL autocomplete box (which works in display text). The getter renders
    /// the stored key as "Display · Key"; the setter accepts the display text, the key, or the friendly name and
    /// stores the underlying key — falling back to the raw text so a cross-environment key can still be typed.</summary>
    public string CvlValueText
    {
        get
        {
            if (string.IsNullOrEmpty(Value)) return "";
            var match = CvlOptions.FirstOrDefault(o => string.Equals(o.Key, Value, StringComparison.OrdinalIgnoreCase));
            return match?.Search ?? Value;
        }
        set
        {
            var text = value?.Trim() ?? "";
            var match = CvlOptions.FirstOrDefault(o =>
                string.Equals(o.Search, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(o.Key, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(o.Display, text, StringComparison.OrdinalIgnoreCase));
            Value = match?.Key ?? text;
        }
    }

    /// <summary>Friendly name of the currently-selected CVL key, shown as a hint beside the picker (empty when
    /// the value isn't a CVL key, is unknown, or already equals its own display).</summary>
    public string ResolvedCvlDisplay
    {
        get
        {
            if (ValueKind != ValueKind.Cvl || string.IsNullOrEmpty(Value)) return "";
            var match = CvlOptions.FirstOrDefault(o => string.Equals(o.Key, Value, StringComparison.OrdinalIgnoreCase));
            return match is null || string.Equals(match.Display, match.Key, StringComparison.Ordinal) ? "" : match.Display;
        }
    }

    private void ResolveValueKind()
    {
        var dt = _meta?.DataTypeOf(FieldTypeId);
        var isCvl = _meta?.IsCvlField(FieldTypeId) == true
            || string.Equals(dt, "CVL", StringComparison.OrdinalIgnoreCase);

        CvlOptions = isCvl && _meta is not null ? _meta.CvlValuesFor(FieldTypeId) : [];
        CvlSearchStrings = CvlOptions.Select(o => o.Search).ToList();
        OnPropertyChanged(nameof(CvlOptions));
        OnPropertyChanged(nameof(CvlSearchStrings));

        ValueKind = isCvl
            ? ValueKind.Cvl
            : dt switch
            {
                "Boolean" => ValueKind.Bool,
                "DateTime" => ValueKind.Date,
                "Integer" or "Double" => ValueKind.Number,
                _ => ValueKind.Text,
            };
        OnPropertyChanged(nameof(UseCvlPicker));
        OnPropertyChanged(nameof(UseTextEditor));
        OnPropertyChanged(nameof(CvlValueText));
        OnPropertyChanged(nameof(ResolvedCvlDisplay));
    }

    public CriterionModel ToModel() => new()
    {
        FieldTypeId = FieldTypeId?.Trim() ?? "",
        Operator = Operator,
        Value = string.IsNullOrEmpty(Value) ? null : Value,
        Interval = _intervalOriginal,
        Language = string.IsNullOrWhiteSpace(Language) ? null : Language.Trim(),
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
