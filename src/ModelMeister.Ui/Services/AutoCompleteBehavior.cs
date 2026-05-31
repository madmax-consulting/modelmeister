using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Turns an <see cref="AutoCompleteBox"/> into a click-to-open, filter-as-you-type dropdown. Avalonia's
/// <see cref="AutoCompleteBox"/> renders like a plain <see cref="TextBox"/> and only opens its suggestion
/// list once the user types (default <c>MinimumPrefixLength = 1</c>) — so a model-backed id picker is visually
/// indistinguishable from a free-text box. With <see cref="OpenOnFocusProperty"/> set, focusing or clicking the
/// box opens the full list immediately (pair with <c>MinimumPrefixLength = 0</c> so an empty box matches every
/// item) and typing filters it. The dropdown open is posted to the dispatcher so the control's own focus
/// handling doesn't immediately re-close it.
/// </summary>
public static class AutoCompleteBehavior
{
    public static readonly AttachedProperty<bool> OpenOnFocusProperty =
        AvaloniaProperty.RegisterAttached<AutoCompleteBox, bool>("OpenOnFocus", typeof(AutoCompleteBehavior));

    public static void SetOpenOnFocus(AutoCompleteBox obj, bool value) => obj.SetValue(OpenOnFocusProperty, value);
    public static bool GetOpenOnFocus(AutoCompleteBox obj) => obj.GetValue(OpenOnFocusProperty);

    static AutoCompleteBehavior()
    {
        OpenOnFocusProperty.Changed.AddClassHandler<AutoCompleteBox>((box, args) =>
        {
            // Idempotent re-wire: drop any prior handlers before (re-)attaching.
            box.GotFocus -= OnGotFocus;
            box.PointerReleased -= OnPointerReleased;
            if (args.GetNewValue<bool>())
            {
                box.GotFocus += OnGotFocus;
                box.PointerReleased += OnPointerReleased;
            }
        });
    }

    private static void OnGotFocus(object? sender, GotFocusEventArgs e) => Open(sender);
    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => Open(sender);

    private static void Open(object? sender)
    {
        if (sender is not AutoCompleteBox box) return;
        // Defer: setting IsDropDownOpen synchronously inside GotFocus is swallowed by the control's own
        // focus/selection handling. Re-check focus on the posted callback so we don't pop open a box the
        // user has already tabbed away from.
        Dispatcher.UIThread.Post(() =>
        {
            if (box.IsKeyboardFocusWithin || box.IsFocused) box.IsDropDownOpen = true;
        });
    }
}
