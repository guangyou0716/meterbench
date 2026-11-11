using System.Net;
using System.Net.Sockets;
using Meterbench.Core.Adapters;

namespace Meterbench.Core.Mqtt;

/// <summary>Raised when the broker refuses the connection, answers with something unexpected, or times out.</summary>
public sealed class MqttConnectException : Exception
{
    public MqttConnectException(string message, byte? returnCode = null)
        : base(message)
    {
        ReturnCode = returnCode;
    }

    /// <summary>CONNACK return code when the broker refused; null for protocol or timeout errors.</summary>
    public byte? ReturnCode { get; }
}

/// <summary>
/// Publish-only MQTT 3.1.1 client over plain TCP: QoS 0, clean session, no credentials, no TLS.
/// Meant for a local broker on loopback. Publishes are fire-and-forget, as QoS 0 is.
/// Not thread-safe: one caller at a time.
/// </summary>
public sealed class MqttPublisher : IMqttReadingPublisher, IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private bool _disposed;

    private MqttPublisher(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    /// <summary>
    /// Opens TCP, sends CONNECT and waits for a CONNACK with return code 0, all within <paramref name="timeout"/>.
    /// </summary>
    public static async Task<MqttPublisher> ConnectAsync(
        IPEndPoint broker,
        string clientId,
        TimeSpan timeout,
        ushort keepAliveSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);
        MqttCodec.ValidateClientId(clientId);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        var client = new TcpClient(broker.AddressFamily) { NoDelay = true };
        try
        {
            await client.ConnectAsync(broker, deadline.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            await stream.WriteAsync(MqttCodec.EncodeConnect(clientId, keepAliveSeconds), deadline.Token).ConfigureAwait(false);
            var connAck = await ReadOnePacketAsync(stream, deadline.Token).ConfigureAwait(false);
            byte code;
            try
            {
                code = MqttCodec.ReadConnAckReturnCode(connAck);
            }
            catch (FormatException ex)
            {
                throw new MqttConnectException($"Broker {broker} did not answer CONNECT with a CONNACK: {ex.Message}");
            }

            if (code != 0)
            {
                throw new MqttConnectException($"Broker {broker} refused the connection (CONNACK return code {code}).", code);
            }

            return new MqttPublisher(client);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new MqttConnectException($"No CONNACK from {broker} within {timeout.TotalMilliseconds:0} ms.");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async ValueTask PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var frame = MqttCodec.EncodePublish(topic, payload.Span);
        await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends DISCONNECT (best effort) and closes the socket.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await _stream.WriteAsync(MqttCodec.EncodeDisconnect()).ConfigureAwait(false);
            await _stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // Broker already gone; nothing left to tell it.
        }
        finally
        {
            _client.Dispose();
        }
    }

    private static async Task<MqttPacket> ReadOnePacketAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[256];
        var filled = 0;
        while (true)
        {
            MqttPacket packet;
            bool complete;
            try
            {
                complete = MqttCodec.TryReadPacket(buffer.AsMemory(0, filled), out packet, out _);
            }
            catch (FormatException ex)
            {
                throw new MqttConnectException($"Broker sent a malformed reply before CONNACK: {ex.Message}");
            }

            if (complete)
            {
                return packet;
            }

            if (filled == buffer.Length)
            {
                throw new MqttConnectException("Broker sent an oversized reply before CONNACK.");
            }

            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new MqttConnectException("Broker closed the connection before CONNACK.");
            }

            filled += read;
        }
    }
}
