using System.Net;
using System.Net.Sockets;

namespace Meterbench.Core.Modbus;

/// <summary>
/// Read-only Modbus TCP server for the synthetic fleet. Loopback only: it refuses to bind
/// any non-loopback address so the demo cannot be exposed on a network by accident.
/// One request at a time per connection; at most <see cref="MaxConnections"/> clients.
/// A malformed MBAP header closes that connection.
/// </summary>
public sealed class ModbusTcpSimulator : IAsyncDisposable
{
    public const int MaxConnections = 8;

    private readonly FleetHoldingRegisters _registers;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(MaxConnections, MaxConnections);
    private readonly TimeSpan _idleTimeout;
    private Task? _acceptLoop;
    private int _disposed;

    public ModbusTcpSimulator(FleetHoldingRegisters registers, IPEndPoint endpoint, TimeSpan? idleTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(registers);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!IPAddress.IsLoopback(endpoint.Address))
        {
            throw new ArgumentException("The Modbus simulator only binds loopback addresses.", nameof(endpoint));
        }

        _registers = registers;
        _listener = new TcpListener(endpoint);
        _idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>The bound endpoint (useful when started on port 0).</summary>
    public IPEndPoint LocalEndpoint => (IPEndPoint)_listener.LocalEndpoint;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_acceptLoop is not null)
        {
            throw new InvalidOperationException("Already started.");
        }

        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_stop.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        var clients = new List<Task>();
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _slots.WaitAsync(token).ConfigureAwait(false);
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                }
                catch
                {
                    _slots.Release();
                    throw;
                }

                clients.RemoveAll(t => t.IsCompleted);
                clients.Add(ServeAsync(client, token));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException) when (token.IsCancellationRequested)
        {
        }

        await Task.WhenAll(clients).ConfigureAwait(false);
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var header = new byte[ModbusTcpCodec.MbapHeaderLength];
                while (!token.IsCancellationRequested)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                    idle.CancelAfter(_idleTimeout);
                    if (!await ReadExactAsync(stream, header, idle.Token).ConfigureAwait(false))
                    {
                        return;
                    }

                    var total = ModbusTcpCodec.AduLength(header);
                    if (total is null)
                    {
                        return;
                    }

                    var adu = new byte[total.Value];
                    header.CopyTo(adu, 0);
                    if (!await ReadExactAsync(stream, adu.AsMemory(header.Length), idle.Token).ConfigureAwait(false))
                    {
                        return;
                    }

                    var response = ModbusTcpCodec.Handle(adu, _registers);
                    if (response is null)
                    {
                        return;
                    }

                    await stream.WriteAsync(response, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _slots.Release();
        }
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken token)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer[read..], token).ConfigureAwait(false);
            if (n == 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }
}
