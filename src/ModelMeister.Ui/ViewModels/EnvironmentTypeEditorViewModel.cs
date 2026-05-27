using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// View-model behind the Create / Edit environment-type dialog. Edits a working copy and only the
/// caller commits it to the registry on confirm. Built-in types are fully editable except their
/// stable <see cref="EnvironmentType.Key"/>. Exposes a live pill preview that tracks the picked color
/// and shorthand.
/// </summary>
public partial class EnvironmentTypeEditorViewModel : ViewModelBase
{
    public EnvironmentTypeEditorViewModel(EnvironmentType? existing)
    {
        IsEdit = existing is not null;
        var src = existing ?? new EnvironmentType { ColorHex = "#1F6FE8", Shorthand = "ENV" };
        Key = src.Key;
        IsBuiltIn = src.IsBuiltIn;
        SortOrder = src.SortOrder;
        _name = src.Name;
        _shorthand = src.Shorthand;
        _description = src.Description ?? "";
        _isProtected = src.IsProtected;
        _color = EnvironmentTypeColors.ToColor(src.ColorHex);
    }

    /// <summary>True when editing an existing type (built-in or custom).</summary>
    public bool IsEdit { get; }
    /// <summary>True for the seven shipped types — name/color/etc. are editable but the type can't be deleted.</summary>
    public bool IsBuiltIn { get; }
    /// <summary>Stable key, preserved across edits (empty for a brand-new custom type — the registry assigns one).</summary>
    public string Key { get; }
    private int SortOrder { get; }

    public string Title => IsEdit
        ? (IsBuiltIn ? "Edit built-in type" : "Edit environment type")
        : "New environment type";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _shorthand;

    [ObservableProperty] private string _description;
    [ObservableProperty] private bool _isProtected;
    [ObservableProperty] private Color _color;
    [ObservableProperty] private string _validation = "";

    /// <summary>The picked color as a hex string (drives <see cref="EnvironmentType.ColorHex"/>).</summary>
    public string ColorHex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";

    // Live preview pill — recomputed as the user edits color / shorthand.
    public IBrush PreviewStrong => EnvironmentTypeColors.Strong(ColorHex);
    public IBrush PreviewSoft => EnvironmentTypeColors.Soft(ColorHex);
    public string PreviewText => string.IsNullOrWhiteSpace(Shorthand) ? "ENV" : Shorthand.Trim();

    partial void OnColorChanged(Color value)
    {
        OnPropertyChanged(nameof(ColorHex));
        OnPropertyChanged(nameof(PreviewStrong));
        OnPropertyChanged(nameof(PreviewSoft));
    }

    partial void OnShorthandChanged(string value) => OnPropertyChanged(nameof(PreviewText));

    /// <summary>Project the form into an <see cref="EnvironmentType"/> for <see cref="IEnvironmentTypeRegistry.Upsert"/>.</summary>
    public EnvironmentType ToType() => new()
    {
        Key = Key,
        Name = Name.Trim(),
        Shorthand = Shorthand.Trim(),
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        ColorHex = ColorHex,
        IsProtected = IsProtected,
        IsBuiltIn = IsBuiltIn,
        SortOrder = SortOrder,
    };

    public bool? Result { get; private set; }
    public event Action? Closed;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Shorthand);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (!CanConfirm()) { Validation = "Name and shorthand are required."; return; }
        Result = true;
        Closed?.Invoke();
    }

    [RelayCommand]
    private void Abort()
    {
        Result = false;
        Closed?.Invoke();
    }
}
