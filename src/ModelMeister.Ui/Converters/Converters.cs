using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ModelMeister.Ui.Models;

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
        "Fields", "Cvls", "LinkTypes", "Roles",
    };
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !Specialized.Contains(s);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Resolves a string resource key (e.g. <c>"IcoCompare"</c>) to its <see cref="Geometry"/>.</summary>
public sealed class ResourceKeyToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key && !string.IsNullOrEmpty(key)
            ? ConverterHelpers.Resource<Geometry>(key)
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
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
        => throw new NotSupportedException();
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
        => throw new NotSupportedException();
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
        => throw new NotSupportedException();
}

/// <summary>true &#8594; success-green brush, false &#8594; danger-red brush.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConverterHelpers.Brush(value is true ? "DiffAddBrush" : "DiffDeleteBrush");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
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
        => throw new NotSupportedException();
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
        => throw new NotSupportedException();
}

/// <summary>
/// Resolves an <see cref="EnvironmentStage"/> (or its string form) to a brush resource.
/// Pass parameter "soft" to get the muted background brush instead of the foreground brush.
/// </summary>
public sealed class StageToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var stage = value?.ToString() ?? nameof(EnvironmentStage.Unspecified);
        var soft = (parameter as string) == "soft";
        return ConverterHelpers.Brush(stage switch
        {
            "Prod"  => soft ? "StageProdSoftBrush"   : "StageProdBrush",
            "Stage" => soft ? "StageProdSoftBrush"   : "StageProdBrush",
            "UAT"   => soft ? "StageTestSoftBrush"   : "StageTestBrush",
            "QA"    => soft ? "StageTestSoftBrush"   : "StageTestBrush",
            "Test"  => soft ? "StageTestSoftBrush"   : "StageTestBrush",
            "Dev"   => soft ? "StageDevSoftBrush"    : "StageDevBrush",
            _       => soft ? "StageUnspecSoftBrush" : "StageUnspecBrush",
        });
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Renders the short uppercase tag shown in the stage pill: PROD / TEST / DEV / ENV.</summary>
public sealed class StageToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value?.ToString()) switch
        {
            "Prod"  => "PROD",
            "Stage" => "STAGE",
            "UAT"   => "UAT",
            "QA"    => "QA",
            "Test"  => "TEST",
            "Dev"   => "DEV",
            _       => "ENV",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns true when the stage value resolves to <see cref="EnvironmentStage.Prod"/>.</summary>
public sealed class StageIsProdConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), nameof(EnvironmentStage.Prod), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns true when the bound string is non-null and non-empty (useful for IsVisible bindings on validation messages, etc.).</summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
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
        => throw new NotSupportedException();
}

/// <summary>Maps a <see cref="Services.LogLevel"/>-derived string to its themed brush.</summary>
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
        => throw new NotSupportedException();
}
