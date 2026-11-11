using System.Net.Sockets;
using Meterbench.Api.Hosting;
using Meterbench.Api.Services;
using Meterbench.Core.Fleet;
using Meterbench.Core.Modbus;
using Microsoft.Extensions.DependencyInjection;

namespace Meterbench.Tests.Api;

public class ApiModbusHostTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly Dictionary<string, string?> Enabled = new()
    {
        ["Meterbench:Modbus:Enabled"] = "true",
        ["Meterbench:Modbus:Port"] = "0"
    };

    [Fact]
    public void DisabledByDefault_NoListener()
    {
        using var api = new TempApi();
        var hosted = api.Services.GetRequiredService<ModbusSimulatorHostedService>();
        Assert.Null(hosted.LocalEndpoint);
    }

    [Fact]
    public async Task Enabled_ServesRegistersThatFollowCommittedTicksAndReset()
    {
        using var api = new TempApi(Enabled);
        var hosted = api.Services.GetRequiredService<ModbusSimulatorHostedService>();
        var endpoint = Assert.IsType<System.Net.IPEndPoint>(hosted.LocalEndpoint);
        Assert.True(System.Net.IPAddress.IsLoopback(endpoint.Address));

        using var client = new TcpClient();
        await client.ConnectAsync(endpoint).WaitAsync(Timeout);
        var stream = client.GetStream();

        // Tick 0: MTR-A1 voltage 230.0 V -> 2300.
        var voltage = await ReadAsync(stream, 1, 1, 0, 2);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x08, 0xFC }, voltage);

        var tick = await TempApi.PostAsync(api.Client, "/api/simulator/tick?count=5");
        tick.EnsureSuccessStatusCode();
        Assert.Equal(5, api.Services.GetRequiredService<SimulatorClock>().Current.Tick);

        // Tick 5: LC-B1 cabinet temperature 41.0 C -> 410.
        var temperature = await ReadAsync(stream, 2, 3, 4, 2);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x01, 0x9A }, temperature);

        // Modbus agrees with the stored latest reading for the same tick.
        var expected = ReadingSimulator(5, "MTR-A1", QuantityCodes.ActiveEnergyKwh);
        Assert.True(FleetHoldingRegisters.TryScale(expected, -2, out var scaled));
        var energy = await ReadAsync(stream, 3, 1, 8, 2);
        Assert.Equal(scaled, (energy[0] << 24) | (energy[1] << 16) | (energy[2] << 8) | energy[3]);

        var reset = await TempApi.PostAsync(api.Client, "/api/seed/reset");
        reset.EnsureSuccessStatusCode();
        temperature = await ReadAsync(stream, 4, 3, 4, 2);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0xFA }, temperature); // 25.0 C at tick 0
    }

    [Fact]
    public void InvalidPort_FailsStartup()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Meterbench:Modbus:Enabled"] = "true",
            ["Meterbench:Modbus:Port"] = "70000"
        };
        var path = Path.Combine(Path.GetTempPath(), "meterbench-" + Guid.NewGuid().ToString("N") + ".db");
        using var factory = new MeterbenchApiFactory(path, settings);
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static double ReadingSimulator(int tick, string deviceId, string code)
    {
        var device = FleetCatalog.Find(deviceId)!;
        var quantity = device.Quantities.Single(q => q.Code == code);
        return Meterbench.Core.Simulation.ReadingSimulator.ValueAt(FleetCatalog.DefaultSeed, deviceId, quantity, tick);
    }

    private static async Task<byte[]> ReadAsync(NetworkStream stream, ushort transaction, byte unit, ushort start, ushort count)
    {
        await stream.WriteAsync(ModbusTcpCodec.BuildReadRequest(transaction, unit, start, count)).AsTask().WaitAsync(Timeout);
        var length = 9 + (count * 2);
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read)).AsTask().WaitAsync(Timeout);
            Assert.NotEqual(0, n);
            read += n;
        }

        Assert.Equal(0x03, buffer[7]);
        return buffer[9..];
    }
}
