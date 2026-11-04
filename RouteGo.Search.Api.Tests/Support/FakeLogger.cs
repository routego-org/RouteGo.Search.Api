using Microsoft.Extensions.Logging;

namespace RouteGo.Search.Api.Tests.Support;

/// <summary>A logger that keeps every line it is given, so a test can assert on alerts.</summary>
public sealed class FakeLogger<T> : ILogger<T>
{
    /// <summary>Every line logged, in order.</summary>
    public List<LoggedEntry> Entries { get; } = new List<LoggedEntry>();

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new LoggedEntry(logLevel == LogLevel.Error, formatter(state, exception)));
    }

    /// <summary>Whether an error line names the code, the event ID and the reason.</summary>
    public bool HasAlert(string problemCode, string eventId, string reason)
    {
        foreach (LoggedEntry entry in Entries)
        {
            bool isAlert = entry.IsError && entry.Message.StartsWith(problemCode + ":", StringComparison.Ordinal);
            if (isAlert && entry.Message.Contains(eventId, StringComparison.Ordinal) && entry.Message.Contains("reason " + reason, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
