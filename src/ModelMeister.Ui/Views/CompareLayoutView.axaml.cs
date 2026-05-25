using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

/// <summary>
/// Shared chrome for env-vs-env compare pages. The host page supplies its DataGrid (or any
/// content) via the <see cref="Body"/> content property; this control wraps it in the standard
/// header card (env pickers + Compare/Save/Copy + extra actions), bottom bar chart card, and
/// status strip. All bindings target an <see cref="ViewModels.ICompareViewModel"/> inherited
/// from the host page's DataContext — no DataContext re-binding needed on the consumer side.
/// </summary>
public partial class CompareLayoutView : UserControl
{
    /// <summary>The body content (typically a <see cref="DataGrid"/>) hosted in the middle slot.</summary>
    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<CompareLayoutView, object?>(nameof(Body));

    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>
    /// Page-specific eyebrow label (e.g. "USERS · COMPARE", "SERVER SETTINGS · COMPARE"). Each
    /// compare page should set this so the top-of-page label matches the page the user navigated to,
    /// instead of every compare page showing a generic "COMPARE".
    /// </summary>
    public static readonly StyledProperty<string?> EyebrowProperty =
        AvaloniaProperty.Register<CompareLayoutView, string?>(nameof(Eyebrow), defaultValue: "COMPARE");

    public string? Eyebrow
    {
        get => GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    /// <summary>Optional subtitle override; defaults to the generic compare-page hint.</summary>
    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<CompareLayoutView, string?>(nameof(Subtitle),
            defaultValue: "Pick two environments to compare side-by-side");

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public CompareLayoutView()
    {
        InitializeComponent();
    }

    /// <summary>Bucket-bar click handler — forwards to the host VM's <see cref="BucketToggleState"/>.
    /// Visual feedback (dimming) is bound to <see cref="ConceptDiffCount.IsHidden"/>; the host view's
    /// code-behind subscribes to <see cref="BucketToggleState.Changed"/> to push the negative-set
    /// filter onto its DataGrid.</summary>
    private void OnBucketClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not ConceptDiffCount c) return;
        if (DataContext is not ICompareViewModel vm) return;
        vm.Buckets?.Toggle(c);
    }

    /// <summary>Swap the two environments and let the auto-compare re-run with the sides flipped.
    /// Promotion is one-way (source → target); swapping is how the user promotes the other direction.</summary>
    private void OnSwapClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICompareViewModel vm) return;
        if (vm.Busy) return;
        (vm.LeftEnv, vm.RightEnv) = (vm.RightEnv, vm.LeftEnv);
    }
}
