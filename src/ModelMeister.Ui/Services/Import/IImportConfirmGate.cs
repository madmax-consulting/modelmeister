using System.Collections.Generic;
using System.Threading.Tasks;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.Services.Import;

/// <summary>
/// Abstraction over the destructive-removal confirmation the import workflow shows between Verify and
/// Import (when a plan reports items it would remove). Behind an interface so the workflow engine is
/// testable without opening a window.
/// </summary>
public interface IImportConfirmGate
{
    /// <summary>Show an itemized, stage-aware confirmation. Returns <c>true</c> to proceed.</summary>
    Task<bool> ConfirmDestructiveAsync(string title, string verb, string noun, IReadOnlyList<string> items);
}

/// <summary>Default gate backed by <see cref="DialogHost.ConfirmBulkAsync"/>, capturing the connected
/// environment's name + stage so the prompt can show the prod banner.</summary>
public sealed class ImportConfirmGate : IImportConfirmGate
{
    private readonly string? _envName;
    private readonly EnvironmentStage _stage;

    public ImportConfirmGate(string? envName, EnvironmentStage stage)
    {
        _envName = envName;
        _stage = stage;
    }

    /// <inheritdoc/>
    public Task<bool> ConfirmDestructiveAsync(string title, string verb, string noun, IReadOnlyList<string> items)
        => DialogHost.ConfirmBulkAsync(title, verb, noun, items, _envName, _stage, destructive: true);
}
