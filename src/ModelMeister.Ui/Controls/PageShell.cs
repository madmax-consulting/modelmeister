using Avalonia;
using Avalonia.Controls;

namespace ModelMeister.Ui.Controls;

/// <summary>
/// Uniform page chrome. Wraps a page's main <see cref="ContentControl.Content"/> with a fixed-height
/// header (eyebrow + title + subtitle on the left, an <see cref="Actions"/> slot on the right) and a
/// reserved bottom status row that renders <see cref="Status"/> text and an indeterminate progress
/// sliver when <see cref="IsBusy"/> is true. The status row never collapses — toggling busy state
/// or text never shifts the content above it.
/// </summary>
public class PageShell : ContentControl
{
    public static readonly StyledProperty<string?> EyebrowProperty =
        AvaloniaProperty.Register<PageShell, string?>(nameof(Eyebrow));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PageShell, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<PageShell, string?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<PageShell, object?>(nameof(Actions));

    public static readonly StyledProperty<string?> StatusProperty =
        AvaloniaProperty.Register<PageShell, string?>(nameof(Status));

    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<PageShell, bool>(nameof(IsBusy));

    /// <summary>Small all-caps label rendered above <see cref="Title"/>. Optional.</summary>
    public string? Eyebrow { get => GetValue(EyebrowProperty); set => SetValue(EyebrowProperty, value); }

    /// <summary>Page title rendered with the <c>.h1</c> style.</summary>
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>One-line subtitle / context shown next to the title with the <c>.subtle</c> style.</summary>
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    /// <summary>Right-aligned slot for a page-scoped action group (typically a <see cref="PageActions"/>).</summary>
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    /// <summary>Status text rendered in the reserved bottom row. Replaces the indeterminate progress sliver when not busy.</summary>
    public string? Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    /// <summary>When true, an indeterminate progress sliver is shown in the bottom row. The row height is reserved either way.</summary>
    public bool IsBusy { get => GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
}
