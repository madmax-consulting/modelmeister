using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ModelMeister.Ui.Models;
using ModelMeister.Ui.Services;

namespace ModelMeister.Ui.Converters;

/// <summary>
/// Helpers shared by the one-way value-converters in this file. They each map a small enumerated
/// input (string or enum) to a styled resource (brush/geometry/glyph) defined in
/// <c>Styles/App.axaml</c> or <c>Styles/Diff.axaml</c>.
/// </summary>
internal static class ConverterHelpers
{
    /// <summary>
    /// Look up <paramref name="key"/> from the active theme's resources, returning the typed
    /// resource if found or <paramref name="fallback"/> otherwise.
    /// </summary>
    public static T? Resource<T>(string key, T? fallback = default) where T : class
    {
        var app = Application.Current;
        if (app is null) return fallback;
        return app.TryGetResource(key, app.ActualThemeVariant, out var res) && res is T t ? t : fallback;
    }

    /// <summary>Convenience wrapper returning a brush (defaulting to <see cref="Brushes.Gray"/>).</summary>
    public static IBrush Brush(string key) => Resource<IBrush>(key) ?? Brushes.Gray;
}

/// <summary>True when the bound concept-kind string is NOT one of the kinds that has a specialized
/// rich-column DataGrid (Fields/Cvls/LinkTypes/Roles) — i.e., the fallback two-column grid applies.</summary>
public sealed class KindIsGenericConverter : IValueConverter
{
    public static readonly KindIsGenericConverter Instance = new();
    private static readonly HashSet<string> Specialized = new(StringComparer.Ordinal)
    {
        "EntityTypes", "Fields", "Cvls", "LinkTypes", "Roles",
    };
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !Specialized.Contains(s);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Resolves a string resource key (e.g. <c>"IcoCompare"</c>) to its <see cref="Geometry"/>.</summary>
public sealed class ResourceKeyToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key && !string.IsNullOrEmpty(key)
            ? ConverterHelpers.Resource<Geometry>(key)
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Maps an operation tag ("Add"/"Update"/"Delete"/...) to a themed brush.</summary>
public sealed class OperationToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConverterHelpers.Brush((value as string) switch
        {
            "Add"    => "DiffAddBrush",
            "Update" => "DiffUpdateBrush",
            "Delete" => "DiffDeleteBrush",
            "Other"  => "DiffWarnBrush",
            _        => "DiffNeutralBrush",
        });

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Classifies a <c>ModelChange</c> type-name into its semantic operation bucket.</summary>
public sealed class KindToOperationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string ?? "";
        if (name.StartsWith("Add", StringComparison.Ordinal)) return "Add";
        if (name.StartsWith("Update", StringComparison.Ordinal)
            || name.StartsWith("Change", StringComparison.Ordinal)) return "Update";
        if (name.StartsWith("Delete", StringComparison.Ordinal)
            || name.StartsWith("Remove", StringComparison.Ordinal)
            || name.StartsWith("Deactivate", StringComparison.Ordinal)) return "Delete";
        return "Other";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Maps a validation severity name ("Error"/"Warning"/...) to a themed brush.</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConverterHelpers.Brush((value as string) switch
        {
            "Error"   => "DiffDeleteBrush",
            "Warning" => "DiffWarnBrush",
            _         => "DiffNeutralBrush",
        });

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>
/// Compare-page "in sync" empty state gate. Inputs (in order): LeftEnv, RightEnv, Busy, HasRows.
/// Returns true only when both environments are selected, no compare is running, and there are no
/// difference rows — i.e. the two environments matched. Lets the shared CompareLayoutView render one
/// uniform "in sync" affirmation across every hub instead of each grid going silently blank.
/// </summary>
public sealed class CompareInSyncConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 4) return false;
        var left = values[0];
        var right = values[1];
        var busy = values[2] is true;
        var hasRows = values[3] is true;
        return left is not null && right is not null && !busy && !hasRows;
    }
}

/// <summary>true &#8594; danger (red) brush, false &#8594; neutral accent brush. Used by the apply-confirmation dialog to escalate styling only for destructive batches.</summary>
public sealed class DangerAccentBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConverterHelpers.Brush(value is true ? "Danger" : "Accent");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>true &#8594; success-green brush, false &#8594; danger-red brush.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConverterHelpers.Brush(value is true ? "DiffAddBrush" : "DiffDeleteBrush");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Maps an import row's <c>RowRunState</c> to a themed brush for the live status pill.</summary>
public sealed class RowStateToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConverterHelpers.Brush(value is ModelMeister.Ui.Services.Import.RowRunState s
            ? s switch
            {
                ModelMeister.Ui.Services.Import.RowRunState.Created or
                ModelMeister.Ui.Services.Import.RowRunState.Updated => "Success",
                ModelMeister.Ui.Services.Import.RowRunState.Failed => "Danger",
                ModelMeister.Ui.Services.Import.RowRunState.Running => "Accent",
                _ => "TextDim",
            }
            : "TextDim");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Maps an import row's <c>RowPlanKind</c> (from Verify) to a themed brush.</summary>
public sealed class RowPlanToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConverterHelpers.Brush(value is ModelMeister.Ui.Services.Import.RowPlanKind k
            ? k switch
            {
                ModelMeister.Ui.Services.Import.RowPlanKind.WillCreate => "Success",
                ModelMeister.Ui.Services.Import.RowPlanKind.WillUpdate => "Accent",
                ModelMeister.Ui.Services.Import.RowPlanKind.Invalid => "Danger",
                _ => "TextDim",
            }
            : "TextDim");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Connection-state badge color: green when connected, accent while connecting, red when faulted.</summary>
public sealed class ConnectionStateToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConverterHelpers.Brush((value as string) switch
        {
            "Connected"  => "Success",
            "Connecting" => "Accent",
            "Faulted"    => "Danger",
            _            => "TextDim",
        });

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Connection-state to a string the icon-font template can render.</summary>
public sealed class ConnectionStateToGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string) switch
        {
            "Connected"  => "Connected",
            "Connecting" => "Connecting",
            "Faulted"    => "Error",
            _            => "Offline",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>
/// Resolves an environment <see cref="EnvironmentType.Key"/> (or a legacy stage name) to its pill
/// brush via the <see cref="EnvironmentTypeRegistry"/>. The strong color is used for text/border; pass
/// parameter "soft" for the translucent background variant. Falls back to neutral gray when the
/// registry isn't wired yet (design-time) or the key is unknown.
/// </summary>
public sealed class StageToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = EnvironmentTypeRegistry.Current?.Resolve(value?.ToString()).ColorHex ?? "#6B7280";
        return (parameter as string) == "soft"
            ? EnvironmentTypeColors.Soft(hex)
            : EnvironmentTypeColors.Strong(hex);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Renders the short tag shown in the environment-type pill (e.g. PROD / TEST / DEV / ENV),
/// resolved from the type's <see cref="EnvironmentType.Shorthand"/>.</summary>
public sealed class StageToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => EnvironmentTypeRegistry.Current?.Resolve(value?.ToString()).Shorthand ?? "ENV";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Turns an environment-type hex color string straight into a pill brush — the strong color
/// for text/border, or the translucent "soft" variant when the converter parameter is "soft". Unlike
/// <see cref="StageToBrushConverter"/> (which resolves a key through the registry), this binds the
/// type's own <see cref="EnvironmentType.ColorHex"/>, so the types-management pill repaints live as the
/// color is edited.</summary>
public sealed class HexToPillBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (parameter as string) == "soft"
            ? EnvironmentTypeColors.Soft(value as string)
            : EnvironmentTypeColors.Strong(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Returns true when the environment type for the bound key is marked protected (drives the
/// destructive-operation safety banner).</summary>
public sealed class StageIsProdConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => EnvironmentTypeRegistry.Current?.IsProtected(value?.ToString()) ?? false;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Returns true when the bound string is non-null and non-empty (useful for IsVisible bindings on validation messages, etc.).</summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>
/// Multi-binding converter that takes (fraction, container-width) and returns <c>fraction *
/// containerWidth</c> in DIPs, clamped to ≥ 1. Used by the compare-envs bottom bar chart to
/// size the foreground fill against its parent grid.
/// </summary>
public sealed class FractionWidthConverter : IMultiValueConverter
{
    public static readonly FractionWidthConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return 0d;
        var fraction = values[0] is double f ? f : 0d;
        var width = values[1] is double w ? w : 0d;
        if (width <= 0 || fraction <= 0) return 0d;
        return Math.Max(1, fraction * width);
    }
}

/// <summary>
/// Maps a 0..1 fraction to a pixel value. The <c>ConverterParameter</c> is the maximum (double or
/// string parseable as one); the output is <c>clamp(fraction, 0, 1) * max</c>. Used by the dashboard
/// 14-day backup sparkline (height) and pending-changes bars (when a container width isn't bound).
/// </summary>
public sealed class FractionToPixelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 0d,
        };
        var max = parameter switch
        {
            double p => p,
            int pi => pi,
            string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            _ => 0d,
        };
        if (max <= 0) return 0d;
        var clamped = Math.Max(0d, Math.Min(1d, fraction));
        return clamped * max;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Maps a <see cref="Services.LogLevel"/>-derived string to its themed brush.</summary>
/// <summary>Joins an <see cref="IEnumerable"/> of strings with ", ". Used for the Roles column.</summary>
public sealed class JoinStringsConverter : IValueConverter
{
    public static readonly JoinStringsConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        if (value is IEnumerable<string> strs) return string.Join(", ", strs);
        if (value is IEnumerable e) return string.Join(", ", e.Cast<object?>().Select(o => o?.ToString() ?? ""));
        return value.ToString();
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConverterHelpers.Brush(value?.ToString() switch
        {
            "Success" => "Success",
            "Warn"    => "Warn",
            "Error"   => "Danger",
            _         => "Info",
        });

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}
