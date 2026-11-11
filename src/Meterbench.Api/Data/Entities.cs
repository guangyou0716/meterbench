namespace Meterbench.Api.Data;

public sealed class ReadingRow
{
    public long Id { get; set; }

    public required string DeviceId { get; set; }

    public int Tick { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public required string Quantity { get; set; }

    public double Value { get; set; }

    public required string Unit { get; set; }

    public required string Obis { get; set; }
}

public sealed class AlarmRow
{
    public long Id { get; set; }

    public required string DeviceId { get; set; }

    public required string Quantity { get; set; }

    public required string Direction { get; set; }

    public double Threshold { get; set; }

    public double Hysteresis { get; set; }

    public double TriggerValue { get; set; }

    public double? ClearValue { get; set; }

    public int RaisedAtTick { get; set; }

    public int? ClearedAtTick { get; set; }

    public bool IsActive { get; set; }

    public bool Acknowledged { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }
}

public sealed class SimulatorStateRow
{
    public int Id { get; set; }

    public int Seed { get; set; }

    public int Tick { get; set; }
}
