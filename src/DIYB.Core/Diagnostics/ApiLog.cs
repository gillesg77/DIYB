using System.Collections.Concurrent;

namespace DIYB.Core.Diagnostics;

/// <summary>Journal circulaire des échanges avec les appareils.</summary>
public sealed class ApiLog
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly int _capacity;

    public ApiLog(int capacity = 2000) => _capacity = capacity;

    public event EventHandler<LogEntry>? EntryAdded;

    public void Add(LogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > _capacity)
            _entries.TryDequeue(out _);

        EntryAdded?.Invoke(this, entry);
    }

    public void Info(string message, string? deviceId = null) => Add(new LogEntry
    {
        Timestamp = DateTimeOffset.Now,
        Level = LogLevel.Info,
        Message = message,
        DeviceId = deviceId,
    });

    public void Warn(string message, string? deviceId = null) => Add(new LogEntry
    {
        Timestamp = DateTimeOffset.Now,
        Level = LogLevel.Warning,
        Message = message,
        DeviceId = deviceId,
    });

    public void Error(string message, string? deviceId = null, ErrorCode? code = null) => Add(new LogEntry
    {
        Timestamp = DateTimeOffset.Now,
        Level = LogLevel.Error,
        Message = message,
        DeviceId = deviceId,
        ErrorCode = code,
    });

    public IReadOnlyList<LogEntry> Snapshot() => _entries.ToArray();

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }
}
