using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace ModelMeister.Ui.Services;

/// <summary>Severity bucket for an <see cref="AppLog"/> entry or toast.</summary>
public enum LogLevel { Info, Success, Warn, Error }

/// <summary>A single line in the in-app log drawer.</summary>
public sealed class LogEntry
{
    public LogEntry(LogLevel level, string source, string message, DateTime? timestamp = null)
    {
        Level = level;
        Source = source;
        Message = message;
        TimestampLocal = timestamp ?? DateTime.Now;
    }

    public LogLevel Level { get; }
    public string Source { get; }
    public string Message { get; }
    public DateTime TimestampLocal { get; }
    public string LevelText => Level.ToString();
    public string Time => TimestampLocal.ToString("HH:mm:ss");
    public bool IsWarnOrError => Level is LogLevel.Warn or LogLevel.Error;
    public bool IsError => Level is LogLevel.Error;
}

/// <summary>An ephemeral toast notification surfaced at the top of the window.</summary>
public sealed class ToastEntry
{
    public ToastEntry(LogLevel level, string title, string? detail, Action? onClick = null)
    {
        Level = level;
        Title = title;
        Detail = detail;
        OnClick = onClick;
        CreatedUtc = DateTime.UtcNow;
    }

    public LogLevel Level { get; }
    public string Title { get; }
    public string? Detail { get; }
    public Action? OnClick { get; }
    public DateTime CreatedUtc { get; }
    public string LevelText => Level.ToString();
}

/// <summary>
/// Abstraction over the process-wide log bus so view-models can be unit-tested without an
/// <see cref="Avalonia.Threading.Dispatcher"/> in play.
/// </summary>
public interface IAppLog
{
    ObservableCollection<LogEntry> Entries { get; }
    ObservableCollection<ToastEntry> Toasts { get; }

    void Info(string source, string message);
    void Success(string source, string message);
    void Warn(string source, string message);
    void Error(string source, string message);

    void Toast(LogLevel level, string title, string? detail = null, Action? onClick = null);
    void DismissToast(ToastEntry entry);
    void Clear();
}

/// <summary>
/// Process-wide, UI-thread-safe log + toast bus. Both collections are <see cref="ObservableCollection{T}"/>
/// so views bind directly. All mutations are marshalled onto the Avalonia UI thread.
/// </summary>
public sealed class AppLog : IAppLog
{
    private const int MaxEntries = 500;
    private const int MaxToasts = 6;
    private static readonly TimeSpan ToastAutoDismissDelay = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    public ObservableCollection<LogEntry> Entries { get; } = [];

    /// <inheritdoc/>
    public ObservableCollection<ToastEntry> Toasts { get; } = [];

    public void Info(string source, string message)    => Append(LogLevel.Info, source, message);
    public void Success(string source, string message) => Append(LogLevel.Success, source, message);
    public void Warn(string source, string message)    => Append(LogLevel.Warn, source, message);
    public void Error(string source, string message)   => Append(LogLevel.Error, source, message);

    /// <inheritdoc/>
    public void Toast(LogLevel level, string title, string? detail = null, Action? onClick = null)
    {
        var entry = new ToastEntry(level, title, detail, onClick);
        Marshal(() =>
        {
            Toasts.Add(entry);
            while (Toasts.Count > MaxToasts) Toasts.RemoveAt(0);
        });

        // Auto-dismiss informational/success toasts after a few seconds; warnings/errors stay until clicked.
        if (level is LogLevel.Info or LogLevel.Success)
            DispatcherTimer.RunOnce(() => DismissToast(entry), ToastAutoDismissDelay);
    }

    /// <inheritdoc/>
    public void DismissToast(ToastEntry entry) => Marshal(() => Toasts.Remove(entry));

    /// <inheritdoc/>
    public void Clear() => Marshal(() => Entries.Clear());

    private void Append(LogLevel level, string source, string message) => Marshal(() =>
    {
        Entries.Add(new LogEntry(level, source, message));
        while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
    });

    private static void Marshal(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
