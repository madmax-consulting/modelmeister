using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ModelMeister.Ui.ViewModels;

namespace ModelMeister.Ui;

/// <summary>
/// Default <see cref="IDataTemplate"/> that maps a <see cref="ViewModelBase"/> to its
/// matching <c>View</c> by convention: <c>Foo.ViewModels.FooViewModel</c> &#8594;
/// <c>Foo.Views.FooView</c>. Wired in <c>App.axaml</c> as an application-level data template.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    /// <summary>Builds the view instance for the bound view-model, or a placeholder if the type is missing.</summary>
    public Control Build(object? data)
    {
        if (data is null) return new TextBlock { Text = "(null)" };

        var viewName = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var viewType = Type.GetType(viewName);

        return viewType is not null
            ? (Control)Activator.CreateInstance(viewType)!
            : new TextBlock { Text = $"Missing view: {viewName}" };
    }

    /// <inheritdoc/>
    public bool Match(object? data) => data is ViewModelBase;
}
