using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;

namespace StokTakip.IntegrationTests.Api.Realtime;

/// <summary>
/// A minimal SignalR client spoken straight over the test server's WebSocket, without the
/// <c>Microsoft.AspNetCore.SignalR.Client</c> package.
/// <para>
/// Two reasons, and the second is the real one. First, that package is not in the local NuGet
/// cache and this machine cannot reach nuget.org from the CLI. Second — and this is why the
/// hand-rolled client stayed even though the package could have been installed from the IDE —
/// what these tests assert is a <b>server</b> contract: who receives which signal. The JSON hub
/// protocol involved is three lines of it (a handshake, a record separator, an invocation frame),
/// so the client library would add a dependency without adding coverage.
/// </para>
/// Connects directly to the hub path instead of negotiating first, which is exactly what a
/// browser client with <c>skipNegotiation</c> does; the negotiate endpoint has its own tests.
/// </summary>
internal sealed class TestHubClient : IAsyncDisposable
{
    /// <summary>The hub protocol terminates every message with 0x1E, not with a frame boundary —
    /// one WebSocket frame may carry several messages, or half of one.</summary>
    private const char RecordSeparator = '\u001e';

    private const string Handshake = "{\"protocol\":\"json\",\"version\":1}";

    private readonly WebSocket _socket;
    private readonly CancellationTokenSource _reading = new();
    private readonly List<HubSignal> _received = [];
    private readonly Lock _gate = new();
    private readonly StringBuilder _pending = new();

    private Task _readLoop = Task.CompletedTask;

    private TestHubClient(WebSocket socket) => _socket = socket;

    /// <summary>Everything the connection has been sent so far, in arrival order.</summary>
    public IReadOnlyList<HubSignal> Received
    {
        get { lock (_gate) return [.. _received]; }
    }

    public static async Task<TestHubClient> ConnectAsync(
        StokTakipFactory factory, string ticket, CancellationToken ct)
    {
        var socket = await factory.Server.CreateWebSocketClient().ConnectAsync(
            new Uri($"ws://localhost/hubs/stok?access_token={Uri.EscapeDataString(ticket)}"), ct);

        var client = new TestHubClient(socket);

        // The handshake is answered with an empty object; anything else means the server refused
        // the protocol and every later assertion would fail for the wrong reason.
        //
        // Read here, on this thread, BEFORE the background loop starts. Starting the loop first
        // makes the two race for the same socket: the loop swallows the handshake reply as if it
        // were a signal, and this read then blocks until the server's next keep-alive ping fifteen
        // seconds later — which is how the first version of this failed.
        await client.SendAsync(Handshake, ct);
        var response = await client.ReadRecordAsync(ct)
            ?? throw new InvalidOperationException("Hub el sikismasina yanit vermeden baglantiyi kapatti.");

        if (response.Trim() != "{}")
            throw new InvalidOperationException($"Beklenmeyen el sikisma yaniti: {response}");

        client._readLoop = Task.Run(client.ReadLoopAsync);

        return client;
    }

    /// <summary>Waits until the connection has received the named signal, or gives up. Returns the
    /// signal so the caller can assert on its payload.</summary>
    public async Task<HubSignal> WaitForAsync(string target, CancellationToken ct, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            var match = Received.FirstOrDefault(s => s.Target == target);
            if (match is not null) return match;

            await Task.Delay(20, ct);
        }

        throw new TimeoutException(
            $"'{target}' sinyali {timeoutMs} ms icinde gelmedi. Gelenler: "
            + (Received.Count == 0 ? "(hicbiri)" : string.Join(", ", Received.Select(s => s.Target))));
    }

    /// <summary>
    /// Gives the connection a moment to receive anything still in flight. Used before asserting a
    /// signal did <b>not</b> arrive — a negative checked with no settling time proves only that the
    /// test was faster than the network.
    /// </summary>
    public Task SettleAsync(CancellationToken ct) => Task.Delay(250, ct);

    private async Task SendAsync(string payload, CancellationToken ct)
        => await _socket.SendAsync(
            Encoding.UTF8.GetBytes(payload + RecordSeparator),
            WebSocketMessageType.Text,
            endOfMessage: true,
            ct);

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_reading.IsCancellationRequested)
            {
                var record = await ReadRecordAsync(_reading.Token);
                if (record is null) break;

                using var document = JsonDocument.Parse(record);

                // Type 1 is an invocation; the server also sends type 6 pings, which are noise here.
                if (!document.RootElement.TryGetProperty("type", out var type) || type.GetInt32() != 1)
                    continue;

                var target = document.RootElement.GetProperty("target").GetString()!;
                var arguments = document.RootElement.TryGetProperty("arguments", out var args)
                    ? args.EnumerateArray()
                        .Select(a => a.ValueKind == JsonValueKind.Null ? null : a.ToString())
                        .ToArray()
                    : [];

                lock (_gate) _received.Add(new HubSignal(target, arguments));
            }
        }
        catch (OperationCanceledException) { }
        // Three ways the socket can die underneath a read that is already in flight, all of them
        // meaning the same thing: the connection is on its way out. TestHost reports it as an
        // IOException wrapping an ObjectDisposedException, which is what made the first attempt at
        // this fix fail. Deliberately not a blanket catch — a JsonException here would mean the
        // server sent something malformed, and that must still fail the test.
        catch (WebSocketException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    private async Task<string?> ReadRecordAsync(CancellationToken ct)
    {
        while (true)
        {
            var buffered = _pending.ToString();
            var separator = buffered.IndexOf(RecordSeparator);
            if (separator >= 0)
            {
                _pending.Remove(0, separator + 1);
                return buffered[..separator];
            }

            var buffer = new byte[8192];
            var result = await _socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;

            _pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
    }

    /// <summary>
    /// Close first, drain second, cancel only as a backstop. The original order did the opposite —
    /// cancel, then close — and that lost roughly one full suite run in four: the read loop stopped,
    /// the server tore its end of the socket down, and <c>CloseAsync</c> then landed on an already
    /// disposed <c>TestWebSocket</c>. The exception surfaced in teardown, so a test that had passed
    /// every one of its assertions was reported red.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            // The state check is a courtesy, not a guarantee: the server can tear its end down
            // between reading it and the call landing. Every transport failure family is caught
            // here for that reason — WebSocketException, the disposed socket, and TestHost's
            // IOException wrapping of it. There is no domain logic in a close, so nothing worth
            // failing a test over can come out of this block.
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch (WebSocketException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
        catch (OperationCanceledException) { }

        // A well-behaved server answers the close with a close frame, which ends the loop on its
        // own. The timer is there for the case where it does not — waiting forever would turn a
        // flaky failure into a hung run, which is worse.
        _reading.CancelAfter(TimeSpan.FromSeconds(2));

        await _readLoop;
        _socket.Dispose();
        _reading.Dispose();
    }
}

/// <summary>One server→client invocation. Arguments are kept as raw JSON text — the signals in
/// this project carry an id or nothing at all, so a typed shape would be more ceremony than help.</summary>
internal sealed record HubSignal(string Target, string?[] Arguments);
