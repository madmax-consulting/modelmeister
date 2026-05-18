using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ModelMeister.Ui.Converters;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Tiny, reusable value-converters surfaced as static fields so XAML can do
/// <c>{x:Static svc:Eq.Compare}</c> instead of allocating <c>UserControl.Resources</c> dictionaries.
/// </summary>
public static class Eq
{
    /// <summary>Returns <c>true</c> when the bound value's <c>ToString()</c> equals the parameter (ordinal).</summary>
    public static readonly IValueConverter Compare = new StringEqualsConverter();
    /// <summary>Returns <c>true</c> when the bound value's <c>ToString()</c> does NOT equal the parameter (ordinal).</summary>
    public static readonly IValueConverter NotCompare = new StringNotEqualsConverter();
    /// <summary>Workflow step glyph: <c>"done"</c> → IcoCheck geometry; anything else → IcoStepCircle.</summary>
    public static readonly IValueConverter StepGlyph = new StepGlyphConverter();
    /// <summary>Returns the down-chevron geometry when expanded, right-chevron otherwise.</summary>
    public static readonly IValueConverter ChevForExpanded = new ChevForExpandedConverter();
    /// <summary>Returns <c>true</c> when the bound numeric value is &gt; 0.</summary>
    public static readonly IValueConverter GtZero = new GreaterThanZeroConverter();
    /// <summary>Returns <c>true</c> when the bound numeric value is == 0 (drives empty-state visibility).</summary>
    public static readonly IValueConverter IsZero = new IsZeroConverter();
    /// <summary>Negates a boolean binding (defaults to <c>true</c> when value isn't a bool).</summary>
    public static readonly IValueConverter BoolNot = new BoolNotConverter();
    /// <summary>Returns <see cref="TextDecorations.Strikethrough"/> when <c>true</c>, <c>null</c> otherwise.</summary>
    public static readonly IValueConverter StrikeIfTrue = new StrikeIfTrueConverter();
    /// <summary>Returns a low opacity (0.45) when <c>true</c>, 1.0 otherwise — used to dim excluded rows.</summary>
    public static readonly IValueConverter OpacityFor = new OpacityForBoolConverter();
}

internal sealed class StrikeIfTrueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TextDecorations.Strikethrough : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class OpacityForBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 0.45 : 1.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class StringNotEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class StepGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = string.Equals(value?.ToString(), "done", StringComparison.Ordinal)
            ? "IcoCheck"
            : "IcoStepCircle";
        return ConverterHelpers.Resource<Geometry>(key);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class GreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            int i    => i > 0,
            long l   => l > 0,
            double d => d > 0,
            _        => false,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class IsZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            int i    => i == 0,
            long l   => l == 0,
            double d => d == 0,
            _        => true,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class BoolNotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}

internal sealed class ChevForExpandedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConverterHelpers.Resource<Geometry>(value is true ? "IcoChevDown" : "IcoChevRight");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool&#8594;Thickness. <c>BoolToThicknessConverter.PROD</c> renders 2-px when true (the prod-guard border).</summary>
public sealed class BoolToThicknessConverter : IValueConverter
{
    /// <summary>Singleton instance used by the PROD guard: 2-px when prod, 0 otherwise.</summary>
    public static readonly BoolToThicknessConverter PROD = new(new Thickness(2), new Thickness(0));

    private readonly Thickness _onTrue;
    private readonly Thickness _onFalse;

    public BoolToThicknessConverter(Thickness onTrue, Thickness onFalse)
    {
        _onTrue = onTrue;
        _onFalse = onFalse;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? _onTrue : _onFalse;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
