namespace Meterbench.Api.Services;

/// <summary>
/// Last committed simulator seed and tick, shared with the Modbus simulator.
/// FleetService publishes after each committed reset or tick, so Modbus reads never
/// see a tick whose rows are not stored yet.
/// </summary>
public sealed class SimulatorClock
{
    private Snapshot _current = new(Meterbench.Core.Fleet.FleetCatalog.DefaultSeed, 0);

    public (int Seed, int Tick) Current
    {
        get
        {
            var snapshot = Volatile.Read(ref _current);
            return (snapshot.Seed, snapshot.Tick);
        }
    }

    public void Publish(int seed, int tick)
    {
        Volatile.Write(ref _current, new Snapshot(seed, tick));
    }

    private sealed record Snapshot(int Seed, int Tick);
}
