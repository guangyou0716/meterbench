using System.Net;
using System.Net.Sockets;
using Meterbench.Core.Fleet;
using Meterbench.Core.Modbus;

namespace Meterbench.Tests.Modbus;

public class ModbusTcpSimulatorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ServesSeveralRequestsOnOneLoopbackConnection()
    {
        var tick = 0;
        await using var server = new ModbusTcpSimulator(
            new FleetHoldingRegisters(FleetCatalog.DefaultSeed, () => Volatile.Read(ref tick)),
            new IPEndPoint(IPAddress.Loopback, 0));
        server.Start();

        using var client = new TcpClient();
        await client.ConnectAsync(server.LocalEndpoint).WaitAsync(Timeout);
        var stream = client.GetStream();

        var first = await RoundTripAsync(stream, ModbusTcpCodec.BuildReadRequest(1, 1, 0, 2), 13);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x08, 0xFC }, first[9..]);

        Volatile.Write(ref tick, 5);
        var second = await RoundTripAsync(stream, ModbusTcpCodec.BuildReadRequest(2, 3, 4, 2), 13);
        Assert.Equal(0x00, second[0]);
        Assert.Equal(0x02, second[1]);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x01, 0x9A }, second[9..]);

        var error = await RoundTripAsync(stream, ModbusTcpCodec.BuildReadRequest(3, 1, 40, 1), 9);
        Assert.Equal(0x83, error[7]);
        Assert.Equal((byte)ModbusExceptionCode.IllegalDataAddress, error[8]);
    }

    [Fact]
    public async Task MalformedHeader_ClosesTheConnection()
    {
        await using var server = new ModbusTcpSimulator(
            new FleetHoldingRegisters(FleetCatalog.DefaultSeed, () => 0),
            new IPEndPoint(IPAddress.Loopback, 0));
        server.Start();

        using var client = new TcpClient();
        await client.ConnectAsync(server.LocalEndpoint).WaitAsync(Timeout);
        var stream = client.GetStream();
        var bad = ModbusTcpCodec.BuildReadRequest(1, 1, 0, 2);
        bad[2] = 0x12;
        await stream.WriteAsync(bad).AsTask().WaitAsync(Timeout);

        var buffer = new byte[16];
        var read = await stream.ReadAsync(buffer).AsTask().WaitAsync(Timeout);
        Assert.Equal(0, read);
    }

    [Fact]
    public async Task FragmentedRequest_IsReassembled()
    {
        await using var server = new ModbusTcpSimulator(
            new FleetHoldingRegisters(FleetCatalog.DefaultSeed, () => 0),
            new IPEndPoint(IPAddress.Loopback, 0));
        server.Start();

        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(server.LocalEndpoint).WaitAsync(Timeout);
        var stream = client.GetStream();
        var request = ModbusTcpCodec.BuildReadRequest(9, 2, 0, 2);
        await stream.WriteAsync(request.AsMemory(0, 3)).AsTask().WaitAsync(Timeout);
        await Task.Delay(50);
        await stream.WriteAsync(request.AsMemory(3)).AsTask().WaitAsync(Timeout);

        var response = await ReadExactAsync(stream, 13);
        Assert.Equal(0x09, response[1]);
        Assert.Equal(0x02, response[6]);
    }

    [Fact]
    public void NonLoopbackEndpoint_IsRejected()
    {
        var registers = new FleetHoldingRegisters(FleetCatalog.DefaultSeed, () => 0);
        Assert.Throws<ArgumentException>(() => new ModbusTcpSimulator(registers, new IPEndPoint(IPAddress.Any, 0)));
        Assert.Throws<ArgumentException>(() =>
            new ModbusTcpSimulator(registers, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 502)));
    }

    [Fact]
    public async Task Dispose_StopsListening()
    {
        var server = new ModbusTcpSimulator(
            new FleetHoldingRegisters(FleetCatalog.DefaultSeed, () => 0),
            new IPEndPoint(IPAddress.Loopback, 0));
        server.Start();
        var endpoint = server.LocalEndpoint;
        await server.DisposeAsync().AsTask().WaitAsync(Timeout);

        using var client = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => client.ConnectAsync(endpoint).WaitAsync(Timeout));
    }

    [Fact]
    public async Task Dispose_IsIdempotent_AndStartAfterDisposeThrows()
    {
        var server = new ModbusTcpSimulator(
            new FleetHoldingRegisters(FleetCatalog.DefaultSeed, () => 0),
            new IPEndPoint(IPAddress.Loopback, 0));
        server.Start();
        Assert.Throws<InvalidOperationException>(server.Start);
        await server.DisposeAsync();
        await server.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(server.Start);
    }

    private static async Task<byte[]> RoundTripAsync(NetworkStream stream, byte[] request, int expectedLength)
    {
        await stream.WriteAsync(request).AsTask().WaitAsync(Timeout);
        return await ReadExactAsync(stream, expectedLength);
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length)
    {
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read)).AsTask().WaitAsync(Timeout);
            Assert.NotEqual(0, n);
            read += n;
        }

        return buffer;
    }
}
