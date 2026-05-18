using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ModelMeister.Ui.Services;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui.Views;

/// <summary>Compare-envs page. Wires the bottom-chart bucket toggle to the inner grid's exclusion
/// filter so clicking a bucket bar both dims the bar and hides those rows from the table.</summary>
public partial class CompareEnvsView : UserControl
{
    public CompareEnvsView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    private CompareEnvsViewModel? _vmHooked;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vmHooked is not null) _vmHooked.Buckets.Changed -= OnBucketsChanged;
        _vmHooked = DataContext as CompareEnvsViewModel;
        if (_vmHooked is not null) _vmHooked.Buckets.Changed += OnBucketsChanged;
    }

    private void OnBucketsChanged(IReadOnlySet<string> hidden)
    {
        if (_vmHooked is null) return;
        if (this.FindControl<DataGrid>("MainGrid") is { } grid)
            GridFilters.SetExcludedValues(grid, _vmHooked.BucketPath, hidden);
    }
}
