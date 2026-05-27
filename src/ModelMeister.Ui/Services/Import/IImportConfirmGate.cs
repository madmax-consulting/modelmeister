using System.Collections.Generic;
using System.Threading.Tasks;

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
/// environment's name + type key so the prompt can show the protected-environment banner.</summary>
public sealed class ImportConfirmGate : IImportConfirmGate
{
    private readonly string? _envName;
    private readonly string? _typeKey;

    public ImportConfirmGate(string? envName, string? typeKey)
    {
        _envName = envName;
        _typeKey = typeKey;
    }

    /// <inheritdoc/>
    public Task<bool> ConfirmDestructiveAsync(string title, string verb, string noun, IReadOnlyList<string> items)
        => DialogHost.ConfirmBulkAsync(title, verb, noun, items, _envName, _typeKey, destructive: true);
}
