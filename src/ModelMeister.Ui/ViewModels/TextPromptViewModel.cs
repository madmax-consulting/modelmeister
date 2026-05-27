using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModelMeister.Ui.ViewModels;

/// <summary>
/// Minimal single-line text-input dialog VM. Used wherever a feature needs one short string from the
/// user (new folder name, rename, new template name) without a bespoke editor.
/// </summary>
public partial class TextPromptViewModel : ViewModelBase
{
    public TextPromptViewModel(string title, string label, string? initial, string? watermark, string confirmLabel)
    {
        Title = title;
        Label = label;
        _text = initial ?? string.Empty;
        Watermark = watermark ?? string.Empty;
        ConfirmLabel = confirmLabel;
    }

    public string Title { get; }
    public string Label { get; }
    public string Watermark { get; }
    public string ConfirmLabel { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _text;

    /// <summary>The dialog result, set just before <see cref="Closed"/> fires.</summary>
    public bool? Result { get; private set; }

    /// <summary>Raised when the dialog should close (Confirm or Cancel).</summary>
    public event Action? Closed;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Text);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Text = Text.Trim();
        Result = true;
        Closed?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = false;
        Closed?.Invoke();
    }
}
