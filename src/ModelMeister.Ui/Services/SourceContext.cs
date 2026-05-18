using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ModelMeister.Ui.Models;

namespace ModelMeister.Ui.Services;

/// <summary>How many sources a feature page is currently operating against.</summary>
public enum SourceMode
{
    /// <summary>One source in slot A. Default for browse/edit pages.</summary>
    Single,
    /// <summary>Two sources (A and B). Pages render a diff.</summary>
    Compare,
}

/// <summary>Kind of artifact occupying a SourceBar slot.</summary>
public enum SourceSlotKind
{
    None,
    LiveEnv,
    Backup,
    Code,
}

/// <summary>
/// What's in one SourceBar slot. <see cref="Env"/> is set for <see cref="SourceSlotKind.LiveEnv"/>;
/// <see cref="BackupPath"/> for <see cref="SourceSlotKind.Backup"/>. Code is metadata-only for now —
/// the loaded-model path lives on <c>MainWindowViewModel</c>.
/// </summary>
public sealed record SourceSlot(SourceSlotKind Kind, EnvironmentEntry? Env = null, string? BackupPath = null, string? Label = null)
{
    public static SourceSlot None { get; } = new(SourceSlotKind.None);

    /// <summary>Display label for the slot chip. Falls back to env name or backup filename.</summary>
    public string Display => Label
        ?? Env?.Name
        ?? (BackupPath is { } p ? System.IO.Path.GetFileNameWithoutExtension(p) : "—");
}

/// <summary>
/// Process-wide state of the current source set. FeaturePages read this and re-query when
/// <see cref="Changed"/> fires. Always-on regardless of which page is visible.
/// </summary>
public sealed class SourceContext : INotifyPropertyChanged
{
    private SourceMode _mode = SourceMode.Single;
    private SourceSlot _slotA = SourceSlot.None;
    private SourceSlot _slotB = SourceSlot.None;

    /// <summary>Single (slot A only) or Compare (A and B).</summary>
    public SourceMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            OnChanged();
        }
    }

    /// <summary>Primary slot. Almost always populated (the connected live env, by default).</summary>
    public SourceSlot SlotA
    {
        get => _slotA;
        set
        {
            if (_slotA == value) return;
            _slotA = value;
            OnChanged();
        }
    }

    /// <summary>Secondary slot. Populated when <see cref="Mode"/> is Compare; ignored otherwise.</summary>
    public SourceSlot SlotB
    {
        get => _slotB;
        set
        {
            if (_slotB == value) return;
            _slotB = value;
            OnChanged();
        }
    }

    public event Action? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? prop = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        Changed?.Invoke();
    }
}
