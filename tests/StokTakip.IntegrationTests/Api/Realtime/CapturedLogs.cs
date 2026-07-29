using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace StokTakip.IntegrationTests.Api.Realtime;

/// <summary>
/// Captures what one logger category writes, so a test can assert on a log line the application
/// emits but exposes nowhere else — the hub's connect/disconnect entries are the only place the
/// resolved user identifier becomes observable from outside.
/// <para>
/// Attached to the running host's <see cref="ILoggerFactory"/> rather than through a rebuilt host:
/// <c>AddProvider</c> refreshes loggers that already exist, and rebuilding would re-run migration
/// and seeding for one assertion. Everything outside the requested category is dropped, so the
/// provider stays cheap for the rest of the run.
/// </para>
/// </summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly string _category;
    private readonly ConcurrentQueue<string> _messages = new();

    private CapturedLogs(string category) => _category = category;

    public static CapturedLogs Attach(IServiceProvider services, Type category)
    {
        var capture = new CapturedLogs(category.FullName!);
        services.GetRequiredService<ILoggerFactory>().AddProvider(capture);

        return capture;
    }

    public IReadOnlyCollection<string> Messages => [.. _messages];

    /// <summary>Waits for a line matching the predicate. Log writes are not synchronous with the
    /// action that caused them — a hub connect is finished before its entry lands.</summary>
    public async Task<string> WaitForAsync(
        Func<string, bool> predicate, CancellationToken ct, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            var match = Messages.FirstOrDefault(predicate);
            if (match is not null) return match;

            await Task.Delay(20, ct);
        }

        throw new TimeoutException(
            $"Beklenen log satiri {timeoutMs} ms icinde yazilmadi. Yazilanlar: "
            + (Messages.Count == 0 ? "(hicbiri)" : string.Join(" | ", Messages)));
    }

    public ILogger CreateLogger(string categoryName)
        => categoryName == _category ? new Sink(_messages) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose() { }

    private sealed class Sink : ILogger
    {
        private readonly ConcurrentQueue<string> _messages;

        public Sink(ConcurrentQueue<string> messages) => _messages = messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _messages.Enqueue(formatter(state, exception));
    }
}
