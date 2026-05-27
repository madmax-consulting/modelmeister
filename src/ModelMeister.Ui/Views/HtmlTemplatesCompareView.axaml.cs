using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ModelMeister.Ui.Services;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

/// <summary>HTML-template compare page. Wires bucket-bar clicks to a negative-set filter on the inner grid.</summary>
public partial class HtmlTemplatesCompareView : UserControl
{
    public HtmlTemplatesCompareView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    private HtmlTemplatesCompareViewModel? _vmHooked;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vmHooked is not null) _vmHooked.Buckets.Changed -= OnBucketsChanged;
        _vmHooked = DataContext as HtmlTemplatesCompareViewModel;
        if (_vmHooked is not null) _vmHooked.Buckets.Changed += OnBucketsChanged;
    }

    private void OnBucketsChanged(IReadOnlySet<string> hidden)
    {
        if (_vmHooked is null) return;
        if (this.FindControl<DataGrid>("MainGrid") is { } grid)
            GridFilters.SetExcludedValues(grid, _vmHooked.BucketPath, hidden);
    }
}
