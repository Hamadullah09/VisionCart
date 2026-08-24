using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VisionCart.Infrastructure.Logging;

public sealed class FileLogOptions
{
    /// <summary>Directory for log files. Relative paths resolve under the content root.</summary>
    public string Directory { get; set; } = "logs";

    public bool Enabled { get; set; } = true;

    /// <summary>Anything below this is dropped before it reaches the queue.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>Days of history to keep. Older files are deleted on roll.</summary>
    public int RetainDays { get; set; } = 14;

    /// <summary>Per-file ceiling. A runaway loop must not fill a shared disk.</summary>
    public long MaxBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Messages waiting to be written. When this fills, new entries are dropped
    /// rather than blocking the request that produced them.
    /// </summary>
    public int QueueCapacity { get; set; } = 8192;
}

/// <summary>
/// A rolling file log, because shared IIS hosting has no console to read.
///
/// Without this the application is undiagnosable in production: stdout goes
/// nowhere, and the ASP.NET Core Module's own stdout capture is meant for
/// startup failures rather than for running an application.
///
/// Three properties matter more than features here:
///
/// <list type="bullet">
/// <item><b>Writing never blocks a request.</b> Entries go onto a bounded queue
/// drained by one background thread. If the disk stalls or the queue fills,
/// entries are dropped and a counter is written when it recovers — a slow log
/// must never become a slow checkout.</item>
/// <item><b>The disk cannot be filled.</b> Files roll daily and at a size cap,
/// and anything past the retention window is deleted. Shared hosting gives you a
/// quota, and exceeding it takes the site down.</item>
/// <item><b>No message is written twice.</b> One writer thread owns the file
/// handle, so there is no interleaving between requests.</item>
/// </list>
///
/// Log entries are written by the application, which is already forbidden from
/// putting clinical values in them (§10). This provider does not attempt to
/// redact — a filter that half-works would be worse than an explicit rule.
/// </summary>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLogOptions _options;
    private readonly string _directory;
    private readonly BlockingCollection<string> _queue;
    private readonly Thread _writer;
    private readonly CancellationTokenSource _stopping = new();

    private DateOnly _currentDay;
    private int _fileIndex;
    private long _written;
    private int _dropped;
    private bool _disposed;

    public FileLoggerProvider(IOptions<FileLogOptions> options, string contentRoot)
    {
        _options = options.Value;

        _directory = Path.IsPathRooted(_options.Directory)
            ? _options.Directory
            : Path.Combine(contentRoot, _options.Directory);

        System.IO.Directory.CreateDirectory(_directory);

        _queue = new BlockingCollection<string>(Math.Max(64, _options.QueueCapacity));
        _currentDay = DateOnly.FromDateTime(DateTime.UtcNow);

        _writer = new Thread(Drain)
        {
            IsBackground = true,
            Name = "visioncart-file-log",
        };
        _writer.Start();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal bool IsEnabled(LogLevel level) =>
        _options.Enabled && level >= _options.MinimumLevel && level != LogLevel.None;

    internal void Enqueue(string entry)
    {
        // TryAdd, never Add: a full queue means the disk cannot keep up, and the
        // right answer is to lose a log line rather than stall a customer.
        if (_queue.IsAddingCompleted) return;
        if (!_queue.TryAdd(entry)) Interlocked.Increment(ref _dropped);
    }

    private void Drain()
    {
        foreach (var entry in _queue.GetConsumingEnumerable())
        {
            try
            {
                var dropped = Interlocked.Exchange(ref _dropped, 0);
                var text = dropped > 0
                    ? $"{Stamp()} warn  VisionCart.Logging  {dropped} log entries were dropped; the writer could not keep up.{Environment.NewLine}{entry}"
                    : entry;

                Write(text);
            }
            catch (Exception ex)
            {
                // A logger that throws takes the process with it. There is
                // nowhere left to report this, so it is deliberately swallowed.
                System.Diagnostics.Debug.WriteLine($"file log write failed: {ex.Message}");
            }
        }
    }

    private void Write(string text)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (today != _currentDay)
        {
            _currentDay = today;
            _fileIndex = 0;
            _written = 0;
            Prune();
        }

        var path = CurrentPath();

        if (_written == 0 && File.Exists(path)) _written = new FileInfo(path).Length;

        if (_written >= _options.MaxBytes)
        {
            _fileIndex++;
            _written = 0;
            path = CurrentPath();
        }

        var bytes = Encoding.UTF8.GetBytes(text + Environment.NewLine);
        using var stream = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: false);

        stream.Write(bytes);
        _written += bytes.Length;
    }

    private string CurrentPath() => Path.Combine(
        _directory,
        _fileIndex == 0
            ? $"visioncart-{_currentDay:yyyy-MM-dd}.log"
            : $"visioncart-{_currentDay:yyyy-MM-dd}.{_fileIndex}.log");

    private void Prune()
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.RetainDays));

        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "visioncart-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
            }
            catch (IOException)
            {
                // Locked by a log viewer or a backup. It will be caught tomorrow.
            }
        }
    }

    internal static string Stamp() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff'Z'");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _queue.CompleteAdding();
        _stopping.Cancel();

        // Bounded: shutdown must not hang on a stuck disk.
        _writer.Join(TimeSpan.FromSeconds(5));

        _queue.Dispose();
        _stopping.Dispose();
    }
}

internal sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel level) => provider.IsEnabled(level);

    public void Log<TState>(
        LogLevel level, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null) return;

        var builder = new StringBuilder()
            .Append(FileLoggerProvider.Stamp()).Append(' ')
            .Append(Abbreviate(level)).Append(' ')
            .Append(category).Append("  ")
            .Append(message);

        if (exception is not null)
        {
            builder.AppendLine().Append(exception);
        }

        provider.Enqueue(builder.ToString());
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info ",
        LogLevel.Warning => "warn ",
        LogLevel.Error => "error",
        LogLevel.Critical => "crit ",
        _ => "     ",
    };
}
