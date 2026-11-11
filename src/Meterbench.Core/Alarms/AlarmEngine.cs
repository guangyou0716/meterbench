namespace Meterbench.Core.Alarms;

public enum AlarmDirection
{
    High,
    Low
}

public static class AlarmDirections
{
    public const string High = "high";
    public const string Low = "low";

    public static string ToWire(AlarmDirection direction)
    {
        return direction switch
        {
            AlarmDirection.High => High,
            AlarmDirection.Low => Low,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown alarm direction.")
        };
    }

    public static AlarmDirection Parse(string direction)
    {
        return direction switch
        {
            High => AlarmDirection.High,
            Low => AlarmDirection.Low,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown alarm direction.")
        };
    }
}

/// <summary>
/// The one active alarm for a device and quantity, if any.
/// Clear uses the threshold and hysteresis stored when the alarm was raised.
/// </summary>
public sealed record ActiveAlarm(AlarmDirection Direction, double Threshold, double Hysteresis);

public abstract record AlarmDecision
{
    private AlarmDecision()
    {
    }

    public sealed record None : AlarmDecision
    {
        public static readonly None Instance = new();

        private None()
        {
        }
    }

    public sealed record Raise(
        AlarmDirection Direction,
        double Threshold,
        double Hysteresis,
        double TriggerValue,
        int Tick) : AlarmDecision;

    public sealed record Clear(double ClearValue, int Tick) : AlarmDecision;
}

/// <summary>
/// Acknowledge flag for an alarm. Mark is idempotent and keeps the original timestamp.
/// Acknowledging does not clear the alarm and does not by itself raise another one.
/// </summary>
public readonly record struct Acknowledgement(bool IsAcknowledged, DateTimeOffset? AcknowledgedAt)
{
    public Acknowledgement Mark(DateTimeOffset when)
    {
        return IsAcknowledged ? this : new Acknowledgement(true, when);
    }
}

/// <summary>
/// Per-device, per-quantity high/low alarms.
/// Raise when value is strictly outside the threshold.
/// Clear a high alarm when value &lt;= threshold - hysteresis.
/// Clear a low alarm when value &gt;= threshold + hysteresis.
/// While an alarm is active, Evaluate returns None (no duplicate raise).
/// </summary>
public static class AlarmEngine
{
    public static AlarmDecision Evaluate(
        double value,
        int tick,
        double? low,
        double? high,
        double hysteresis,
        ActiveAlarm? active)
    {
        if (hysteresis < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hysteresis), hysteresis, "Hysteresis cannot be negative.");
        }

        if (active is null)
        {
            if (high is not null && value > high.Value)
            {
                return new AlarmDecision.Raise(AlarmDirection.High, high.Value, hysteresis, value, tick);
            }

            if (low is not null && value < low.Value)
            {
                return new AlarmDecision.Raise(AlarmDirection.Low, low.Value, hysteresis, value, tick);
            }

            return AlarmDecision.None.Instance;
        }

        if (active.Direction == AlarmDirection.High && value <= active.Threshold - active.Hysteresis)
        {
            return new AlarmDecision.Clear(value, tick);
        }

        if (active.Direction == AlarmDirection.Low && value >= active.Threshold + active.Hysteresis)
        {
            return new AlarmDecision.Clear(value, tick);
        }

        return AlarmDecision.None.Instance;
    }
}
