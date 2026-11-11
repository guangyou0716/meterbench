using Meterbench.Api.Contracts;
using Meterbench.Api.Data;
using Meterbench.Core.Alarms;
using Meterbench.Core.Fleet;
using Meterbench.Core.Simulation;
using Microsoft.EntityFrameworkCore;

namespace Meterbench.Api.Services;

public abstract record LatestLookup
{
    private LatestLookup()
    {
    }

    public sealed record Ok(LatestDto Body) : LatestLookup;

    public sealed record MissingDevice : LatestLookup
    {
        public static readonly MissingDevice Instance = new();

        private MissingDevice()
        {
        }
    }

    public sealed record MissingReading : LatestLookup
    {
        public static readonly MissingReading Instance = new();

        private MissingReading()
        {
        }
    }
}

public abstract record HistoryLookup
{
    private HistoryLookup()
    {
    }

    public sealed record Ok(HistoryDto Body) : HistoryLookup;

    public sealed record MissingDevice : HistoryLookup
    {
        public static readonly MissingDevice Instance = new();

        private MissingDevice()
        {
        }
    }
}

public abstract record AckLookup
{
    private AckLookup()
    {
    }

    public sealed record Ok(AlarmDto Body) : AckLookup;

    public sealed record Missing : AckLookup
    {
        public static readonly Missing Instance = new();

        private Missing()
        {
        }
    }
}

/// <summary>
/// One per process. Serializes simulator writes (seed, reset, tick) so two overlapping
/// requests cannot read the same tick and insert duplicate rows.
/// </summary>
public sealed class SimulatorWriteGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
        }
    }
}

public sealed class FleetService
{
    private readonly MeterbenchDbContext _db;
    private readonly SimulatorWriteGate _gate;

    public FleetService(MeterbenchDbContext db, SimulatorWriteGate gate)
    {
        _db = db;
        _gate = gate;
    }

    public IReadOnlyList<DeviceDto> ListDevices()
    {
        return FleetCatalog.Devices
            .Select(device => new DeviceDto(
                device.Id,
                device.Name,
                device.Kind,
                device.Location,
                device.Quantities.Select(quantity => quantity.Code).ToList()))
            .ToList();
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.SimulatorStates.AnyAsync(cancellationToken))
        {
            return;
        }

        using (await _gate.EnterAsync(cancellationToken))
        {
            await EnsureSeededLockedAsync(cancellationToken);
        }
    }

    public async Task<SeedResetDto> ResetAsync(CancellationToken cancellationToken = default)
    {
        using (await _gate.EnterAsync(cancellationToken))
        {
            return await ResetLockedAsync(cancellationToken);
        }
    }

    private async Task EnsureSeededLockedAsync(CancellationToken cancellationToken)
    {
        if (await _db.SimulatorStates.AnyAsync(cancellationToken))
        {
            return;
        }

        await ResetLockedAsync(cancellationToken);
    }

    private async Task<SeedResetDto> ResetLockedAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.Alarms.ExecuteDeleteAsync(cancellationToken);
        await _db.Readings.ExecuteDeleteAsync(cancellationToken);
        _db.ChangeTracker.Clear();

        var state = await _db.SimulatorStates.SingleOrDefaultAsync(cancellationToken);
        if (state is null)
        {
            state = new SimulatorStateRow
            {
                Id = 1,
                Seed = FleetCatalog.DefaultSeed,
                Tick = 0
            };
            _db.SimulatorStates.Add(state);
        }
        else
        {
            state.Seed = FleetCatalog.DefaultSeed;
            state.Tick = 0;
        }

        var active = new Dictionary<(string DeviceId, string Quantity), AlarmRow>();
        WriteTick(state.Seed, state.Tick, active);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SeedResetDto(state.Seed, state.Tick, FleetCatalog.Devices.Count);
    }

    public async Task<TickDto> TickAsync(int count, CancellationToken cancellationToken = default)
    {
        using var entered = await _gate.EnterAsync(cancellationToken);
        await EnsureSeededLockedAsync(cancellationToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var state = await _db.SimulatorStates.SingleAsync(cancellationToken);
        var active = await LoadActiveAsync(cancellationToken);
        for (var i = 0; i < count; i++)
        {
            state.Tick += 1;
            WriteTick(state.Seed, state.Tick, active);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TickDto(state.Seed, state.Tick, count);
    }

    public async Task<LatestLookup> GetLatestAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var device = FleetCatalog.Find(deviceId);
        if (device is null)
        {
            return LatestLookup.MissingDevice.Instance;
        }

        await EnsureSeededAsync(cancellationToken);
        var tick = await _db.Readings
            .Where(row => row.DeviceId == deviceId)
            .Select(row => (int?)row.Tick)
            .MaxAsync(cancellationToken);
        if (tick is null)
        {
            return LatestLookup.MissingReading.Instance;
        }

        var rows = await _db.Readings
            .Where(row => row.DeviceId == deviceId && row.Tick == tick.Value)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            // A reset removed this tick between the two queries. Report it like an empty device
            // instead of failing with an index error.
            return LatestLookup.MissingReading.Instance;
        }

        var recordedAt = rows[0].RecordedAt;
        return new LatestLookup.Ok(new LatestDto(deviceId, tick.Value, recordedAt, OrderReadings(device, rows)));
    }

    public async Task<HistoryLookup> GetHistoryAsync(
        string deviceId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var device = FleetCatalog.Find(deviceId);
        if (device is null)
        {
            return HistoryLookup.MissingDevice.Instance;
        }

        await EnsureSeededAsync(cancellationToken);
        var ticks = await _db.Readings
            .Where(row => row.DeviceId == deviceId)
            .Select(row => row.Tick)
            .Distinct()
            .OrderByDescending(tick => tick)
            .Take(limit)
            .ToListAsync(cancellationToken);
        if (ticks.Count == 0)
        {
            return new HistoryLookup.Ok(new HistoryDto(deviceId, limit, Array.Empty<HistoryPointDto>()));
        }

        var rows = await _db.Readings
            .Where(row => row.DeviceId == deviceId && ticks.Contains(row.Tick))
            .ToListAsync(cancellationToken);
        // A reset between the two queries can leave a tick with no rows; skip it instead of failing.
        var points = new List<HistoryPointDto>(ticks.Count);
        foreach (var tick in ticks)
        {
            var atTick = rows.Where(row => row.Tick == tick).ToList();
            if (atTick.Count > 0)
            {
                points.Add(new HistoryPointDto(tick, atTick[0].RecordedAt, OrderReadings(device, atTick)));
            }
        }

        return new HistoryLookup.Ok(new HistoryDto(deviceId, limit, points));
    }

    public async Task<IReadOnlyList<AlarmDto>> ListAlarmsAsync(
        bool? active,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        var query = _db.Alarms.AsQueryable();
        if (active is bool activeOnly)
        {
            query = query.Where(row => row.IsActive == activeOnly);
        }

        var rows = await query.OrderBy(row => row.Id).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<AckLookup> AcknowledgeAsync(long id, CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        var row = await _db.Alarms.SingleOrDefaultAsync(alarm => alarm.Id == id, cancellationToken);
        if (row is null)
        {
            return AckLookup.Missing.Instance;
        }

        if (!row.Acknowledged)
        {
            var marked = new Acknowledgement(false, null).Mark(DateTimeOffset.UtcNow);
            row.Acknowledged = marked.IsAcknowledged;
            row.AcknowledgedAt = marked.AcknowledgedAt;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new AckLookup.Ok(ToDto(row));
    }

    private async Task<Dictionary<(string DeviceId, string Quantity), AlarmRow>> LoadActiveAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _db.Alarms.Where(row => row.IsActive).ToListAsync(cancellationToken);
        var active = new Dictionary<(string DeviceId, string Quantity), AlarmRow>();
        foreach (var row in rows)
        {
            active[(row.DeviceId, row.Quantity)] = row;
        }

        return active;
    }

    private void WriteTick(
        int seed,
        int tick,
        Dictionary<(string DeviceId, string Quantity), AlarmRow> active)
    {
        foreach (var device in FleetCatalog.Devices)
        {
            foreach (var quantity in device.Quantities)
            {
                var value = ReadingSimulator.ValueAt(seed, device.Id, quantity, tick);
                _db.Readings.Add(new ReadingRow
                {
                    DeviceId = device.Id,
                    Tick = tick,
                    RecordedAt = FleetCatalog.TimestampForTick(tick),
                    Quantity = quantity.Code,
                    Value = value,
                    Unit = quantity.Unit,
                    Obis = quantity.Obis
                });

                active.TryGetValue((device.Id, quantity.Code), out var current);
                var decision = AlarmEngine.Evaluate(
                    value,
                    tick,
                    quantity.Low,
                    quantity.High,
                    quantity.Hysteresis,
                    current is null
                        ? null
                        : new ActiveAlarm(
                            AlarmDirections.Parse(current.Direction),
                            current.Threshold,
                            current.Hysteresis));

                if (decision is AlarmDecision.Raise raise)
                {
                    if (current is not null)
                    {
                        throw new InvalidOperationException(
                            $"Duplicate raise for {device.Id} {quantity.Code} at tick {tick}.");
                    }

                    var created = new AlarmRow
                    {
                        DeviceId = device.Id,
                        Quantity = quantity.Code,
                        Direction = AlarmDirections.ToWire(raise.Direction),
                        Threshold = raise.Threshold,
                        Hysteresis = raise.Hysteresis,
                        TriggerValue = raise.TriggerValue,
                        RaisedAtTick = raise.Tick,
                        IsActive = true,
                        Acknowledged = false
                    };
                    _db.Alarms.Add(created);
                    active[(device.Id, quantity.Code)] = created;
                }
                else if (decision is AlarmDecision.Clear clear)
                {
                    if (current is null)
                    {
                        throw new InvalidOperationException(
                            $"Clear without an active alarm for {device.Id} {quantity.Code} at tick {tick}.");
                    }

                    current.IsActive = false;
                    current.ClearValue = clear.ClearValue;
                    current.ClearedAtTick = clear.Tick;
                    active.Remove((device.Id, quantity.Code));
                }
            }
        }
    }

    private static List<QuantityReadingDto> OrderReadings(DeviceDefinition device, IEnumerable<ReadingRow> rows)
    {
        var order = new Dictionary<string, int>(device.Quantities.Count);
        for (var i = 0; i < device.Quantities.Count; i++)
        {
            order[device.Quantities[i].Code] = i;
        }

        return rows
            .OrderBy(row => order.TryGetValue(row.Quantity, out var index) ? index : int.MaxValue)
            .ThenBy(row => row.Quantity, StringComparer.Ordinal)
            .Select(row => new QuantityReadingDto(row.Quantity, row.Obis, row.Unit, row.Value))
            .ToList();
    }

    private static AlarmDto ToDto(AlarmRow row)
    {
        return new AlarmDto(
            row.Id,
            row.DeviceId,
            row.Quantity,
            row.Direction,
            row.Threshold,
            row.Hysteresis,
            row.TriggerValue,
            row.ClearValue,
            row.RaisedAtTick,
            row.ClearedAtTick,
            row.IsActive,
            row.Acknowledged,
            row.AcknowledgedAt);
    }
}
