using Avalonia.Data.Converters;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Services;

/// <summary>
/// Static value converters for the query editor: pick the right typed value editor by <see cref="ValueKind"/>.
/// Used via <c>{x:Static svc:QueryValueKindIs.Text}</c> etc., matching the existing <c>Eq.IsZero</c> pattern.
/// </summary>
public static class QueryValueKindIs
{
    public static readonly IValueConverter Text =
        new FuncValueConverter<ValueKind, bool>(k => k == ValueKind.Text);

    public static readonly IValueConverter Bool =
        new FuncValueConverter<ValueKind, bool>(k => k == ValueKind.Bool);

    public static readonly IValueConverter Date =
        new FuncValueConverter<ValueKind, bool>(k => k == ValueKind.Date);

    public static readonly IValueConverter Number =
        new FuncValueConverter<ValueKind, bool>(k => k == ValueKind.Number);

    public static readonly IValueConverter Cvl =
        new FuncValueConverter<ValueKind, bool>(k => k == ValueKind.Cvl);
}
