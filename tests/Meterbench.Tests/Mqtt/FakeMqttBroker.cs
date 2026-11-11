using System.Net;
using System.Net.Sockets;
using Meterbench.Core.Mqtt;

namespace Meterbench.Tests.Mqtt;

/// <summary>
/// In-test single-connection broker on loopback: answers CONNECT with a chosen CONNACK (or never),
/// then records packets until DISCONNECT or the socket closes.
/// </summary>
internal sealed class FakeMqttBroker : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly byte? _connAckCode;
    private readonly byte[]? _rawReply;
    private readonly Task _serve;
    private readonly CancellationTokenSource _stop = new();

    public FakeMqttBroker(byte? connAckCode = 0, byte[]? rawReply = null)
    {
        _connAckCode = connAckCode;
        _rawReply = rawReply;
        _listener.Start();
        _serve = Task.Run(ServeAsync);
    }

    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

    public List<MqttPacket> Received { get; } = [];

    public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _serve.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException or IOException)
        {
        }

        _stop.Dispose();
    }

    private async Task ServeAsync()
    {
        using var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
        var stream = client.GetStream();
        var buffer = new byte[64 * 1024];
        var filled = 0;
        try
        {
            while (true)
            {
                while (MqttCodec.TryReadPacket(buffer.AsMemory(0, filled), out var packet, out var consumed))
                {
                    var copy = new MqttPacket(packet.Type, packet.Flags, packet.Body.ToArray());
                    lock (Received)
                    {
                        Received.Add(copy);
                    }

                    Buffer.BlockCopy(buffer, consumed, buffer, 0, filled - consumed);
                    filled -= consumed;

                    if (copy.Type == MqttPacketType.Connect)
                    {
                        if (_rawReply is not null)
                        {
                            await stream.WriteAsync(_rawReply, _stop.Token).ConfigureAwait(false);
                        }
                        else if (_connAckCode is { } code)
                        {
                            await stream.WriteAsync(new byte[] { 0x20, 0x02, 0x00, code }, _stop.Token).ConfigureAwait(false);
                        }
                    }
                    else if (copy.Type == MqttPacketType.Disconnect)
                    {
                        return;
                    }
                }

                var read = await stream.ReadAsync(buffer.AsMemory(filled), _stop.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                filled += read;
            }
        }
        finally
        {
            Finished.TrySetResult();
        }
    }
}
