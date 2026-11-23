// A tiny file logger: one rolling text file per run in <data dir>/logs. It never logs keys or user text (callers pass
// only types, counts and ids) and it never throws: a full disk must not take the app down.

using Microsoft.Extensions.Logging;

namespace TinyFix.App.Services;

/// <summary>Writes log lines to a file.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 1_000_000;
    private readonly string _path;
    private readonly Lock _gate = new();

    /// <summary>Creates a provider that appends to <paramref name="directory"/>/tinyfix.log (the previous log becomes tinyfix.log.1).</summary>
    public FileLoggerProvider(string directory)
    {
        _path = Path.Combine(directory, "tinyfix.log");
        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                File.Move(_path, _path + ".1", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging stays best effort.
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nowhere else to report it.
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {logLevel,-11} {category}: {formatter(state, exception)}";
            // The exception type and stack are enough to debug; its message could carry user text.
            if (exception is not null)
                text += $" [{exception.GetType().Name}]{Environment.NewLine}{exception.StackTrace}";
            owner.Write(text);
        }
    }
}
