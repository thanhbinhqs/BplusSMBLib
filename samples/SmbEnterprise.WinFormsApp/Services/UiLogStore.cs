using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace SmbEnterprise.WinFormsApp.Services;

public sealed class UiLogStore
{
    private readonly ConcurrentQueue<string> _lines = new();

    public event EventHandler<string>? LogAdded;

    public void Add(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > 2000 && _lines.TryDequeue(out _))
        {
        }

        LogAdded?.Invoke(this, line);
    }

    public IReadOnlyList<string> Snapshot()
    {
        return _lines.ToArray();
    }
}

public sealed class UiLogSink : ILogEventSink
{
    private readonly UiLogStore _store;

    public UiLogSink(UiLogStore store)
    {
        _store = store;
    }

    public void Emit(LogEvent logEvent)
    {
        var line = $"{logEvent.Timestamp:HH:mm:ss} [{logEvent.Level}] {logEvent.RenderMessage()}";
        if (logEvent.Exception is not null)
        {
            line += $" | {logEvent.Exception.GetType().Name}: {logEvent.Exception.Message}";
        }

        _store.Add(line);
    }
}
