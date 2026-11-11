using System.Net;
using Meterbench.Api.Services;
using Meterbench.Core.Mqtt;
using Microsoft.Extensions.Options;

namespace Meterbench.Api.Hosting;

/// <summary>Opt-in settings for publishing fleet readings to a local MQTT broker. Off by default.</summary>
public sealed class MqttPublisherOptions
{
    public const string SectionName = "Meterbench:Mqtt";

    public bool Enabled { get; set; }

    /// <summary>Broker IP address. Only loopback addresses are accepted.</summary>
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1883;

    /// <summary>1..23 ASCII letters or digits.</summary>
    public string ClientId { get; set; } = "meterbench";

    public string TopicPrefix { get; set; } = FleetMqttMessages.DefaultTopicPrefix;

    /// <summary>How often the committed tick is checked for a change.</summary>
    public int PollMilliseconds { get; set; } = 250;

    /// <summary>Wait before reconnecting after the broker is unreachable or drops the connection.</summary>
    public int RetryMilliseconds { get; set; } = 2000;

    public int ConnectTimeoutMilliseconds { get; set; } = 5000;

    /// <summary>Returns null when valid, otherwise the first problem.</summary>
    public string? Validate()
    {
        if (!Enabled)
        {
            return null;
        }

        if (!IPAddress.TryParse(Host, out var address) || !IPAddress.IsLoopback(address))
        {
            return "Meterbench:Mqtt:Host must be a loopback IP address.";
        }

        if (Port is < 1 or > 65535)
        {
            return "Meterbench:Mqtt:Port must be 1..65535.";
        }

        if (PollMilliseconds < 10 || RetryMilliseconds < 10 || ConnectTimeoutMilliseconds < 10)
        {
            return "Meterbench:Mqtt poll, retry and connect timeout must be at least 10 ms.";
        }

        try
        {
            MqttCodec.ValidateClientId(ClientId);
            FleetMqttMessages.TopicFor(TopicPrefix, "x");
        }
        catch (ArgumentException ex)
        {
            return $"Meterbench:Mqtt: {ex.Message}";
        }

        return null;
    }
}

/// <summary>
/// When <c>Meterbench:Mqtt:Enabled</c> is true, publishes one QoS 0 message per device to a loopback
/// broker on connect and after every committed tick or reset. Reconnects after failures; a missing
/// broker never stops the API.
/// </summary>
public sealed class MqttPublisherHostedService : BackgroundService
{
    private readonly IOptions<MqttPublisherOptions> _options;
    private readonly SimulatorClock _clock;
    private readonly ILogger<MqttPublisherHostedService> _logger;
    private long _publishedTicks;

    public MqttPublisherHostedService(
        IOptions<MqttPublisherOptions> options,
        SimulatorClock clock,
        ILogger<MqttPublisherHostedService> logger)
    {
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Number of (seed, tick) snapshots fully published since start.</summary>
    public long PublishedTicks => Interlocked.Read(ref _publishedTicks);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        if (!options.Enabled)
        {
            return;
        }

        var broker = new IPEndPoint(IPAddress.Parse(options.Host), options.Port);
        var poll = TimeSpan.FromMilliseconds(options.PollMilliseconds);
        var retry = TimeSpan.FromMilliseconds(options.RetryMilliseconds);
        var connectTimeout = TimeSpan.FromMilliseconds(options.ConnectTimeoutMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var publisher = await MqttPublisher.ConnectAsync(
                    broker, options.ClientId, connectTimeout, cancellationToken: stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("MQTT publisher connected to {Broker}.", broker);

                (int Seed, int Tick)? last = null;
                while (!stoppingToken.IsCancellationRequested)
                {
                    var current = _clock.Current;
                    if (last != current)
                    {
                        await FleetMqttMessages.PublishFleetAsync(
                            publisher, current.Seed, current.Tick, options.TopicPrefix, stoppingToken).ConfigureAwait(false);
                        last = current;
                        Interlocked.Increment(ref _publishedTicks);
                    }

                    await Task.Delay(poll, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is MqttConnectException or IOException or System.Net.Sockets.SocketException or FormatException)
            {
                _logger.LogWarning("MQTT publisher: {Message} Retrying in {Retry} ms.", ex.Message, options.RetryMilliseconds);
            }

            try
            {
                await Task.Delay(retry, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
