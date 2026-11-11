using System.Net;
using Meterbench.Api.Services;
using Meterbench.Core.Modbus;
using Microsoft.Extensions.Options;

namespace Meterbench.Api.Hosting;

/// <summary>Opt-in settings for the loopback Modbus TCP simulator. Off by default.</summary>
public sealed class ModbusSimulatorOptions
{
    public const string SectionName = "Meterbench:Modbus";

    public bool Enabled { get; set; }

    /// <summary>Loopback TCP port. 0 picks a free port (tests).</summary>
    public int Port { get; set; } = 5020;
}

/// <summary>
/// Starts <see cref="ModbusTcpSimulator"/> on 127.0.0.1 next to the API when
/// <c>Meterbench:Modbus:Enabled</c> is true. Registers follow the API's committed tick.
/// </summary>
public sealed class ModbusSimulatorHostedService : IHostedService, IAsyncDisposable
{
    private readonly IOptions<ModbusSimulatorOptions> _options;
    private readonly SimulatorClock _clock;
    private readonly ILogger<ModbusSimulatorHostedService> _logger;
    private ModbusTcpSimulator? _server;

    public ModbusSimulatorHostedService(
        IOptions<ModbusSimulatorOptions> options,
        SimulatorClock clock,
        ILogger<ModbusSimulatorHostedService> logger)
    {
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Bound endpoint while running; null when disabled or stopped.</summary>
    public IPEndPoint? LocalEndpoint => _server?.LocalEndpoint;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        var registers = new FleetHoldingRegisters(() => _clock.Current);
        var server = new ModbusTcpSimulator(registers, new IPEndPoint(IPAddress.Loopback, options.Port));
        try
        {
            server.Start();
        }
        catch
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }

        _server = server;
        _logger.LogInformation("Modbus TCP simulator listening on {Endpoint} (read-only, loopback).", server.LocalEndpoint);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var server = Interlocked.Exchange(ref _server, null);
        if (server is not null)
        {
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
