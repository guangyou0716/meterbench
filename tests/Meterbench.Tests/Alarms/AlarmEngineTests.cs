using Meterbench.Core.Alarms;
using Meterbench.Core.Fleet;
using Meterbench.Core.Simulation;

namespace Meterbench.Tests.Alarms;

public class AlarmEngineTests
{
    [Fact]
    public void InsideBand_DoesNotRaise()
    {
        var decision = AlarmEngine.Evaluate(50, 1, low: 0, high: 100, hysteresis: 5, active: null);
        Assert.IsType<AlarmDecision.None>(decision);
    }

    [Fact]
    public void ValueEqualToThreshold_DoesNotRaise()
    {
        Assert.IsType<AlarmDecision.None>(AlarmEngine.Evaluate(100, 1, low: 0, high: 100, hysteresis: 5, active: null));
        Assert.IsType<AlarmDecision.None>(AlarmEngine.Evaluate(0, 1, low: 0, high: 100, hysteresis: 5, active: null));
    }

    [Fact]
    public void HighBreach_RaisesOnce()
    {
        var decision = AlarmEngine.Evaluate(100.1, 4, low: 0, high: 100, hysteresis: 5, active: null);
        var raised = Assert.IsType<AlarmDecision.Raise>(decision);
        Assert.Equal(AlarmDirection.High, raised.Direction);
        Assert.Equal(100, raised.Threshold);
        Assert.Equal(5, raised.Hysteresis);
        Assert.Equal(100.1, raised.TriggerValue);
        Assert.Equal(4, raised.Tick);
    }

    [Fact]
    public void LowBreach_RaisesOnce()
    {
        var decision = AlarmEngine.Evaluate(9.9, 2, low: 10, high: 100, hysteresis: 2, active: null);
        var raised = Assert.IsType<AlarmDecision.Raise>(decision);
        Assert.Equal(AlarmDirection.Low, raised.Direction);
        Assert.Equal(10, raised.Threshold);
        Assert.Equal(9.9, raised.TriggerValue);
    }

    [Fact]
    public void ActiveBreach_DoesNotRaiseADuplicate()
    {
        var raised = Assert.IsType<AlarmDecision.Raise>(
            AlarmEngine.Evaluate(110, 1, low: null, high: 100, hysteresis: 5, active: null));
        var active = new ActiveAlarm(raised.Direction, raised.Threshold, raised.Hysteresis);

        Assert.IsType<AlarmDecision.None>(
            AlarmEngine.Evaluate(120, 2, low: null, high: 100, hysteresis: 5, active));
        Assert.IsType<AlarmDecision.None>(
            AlarmEngine.Evaluate(101, 3, low: null, high: 100, hysteresis: 5, active));
    }

    [Fact]
    public void HighAlarm_StaysActiveUntilHysteresisBand_ThenClears()
    {
        var raised = Assert.IsType<AlarmDecision.Raise>(
            AlarmEngine.Evaluate(101, 1, low: 0, high: 100, hysteresis: 5, active: null));
        var active = new ActiveAlarm(raised.Direction, raised.Threshold, raised.Hysteresis);

        Assert.IsType<AlarmDecision.None>(AlarmEngine.Evaluate(100, 2, 0, 100, 5, active));
        Assert.IsType<AlarmDecision.None>(AlarmEngine.Evaluate(96, 3, 0, 100, 5, active));

        var cleared = Assert.IsType<AlarmDecision.Clear>(AlarmEngine.Evaluate(95, 4, 0, 100, 5, active));
        Assert.Equal(95, cleared.ClearValue);
        Assert.Equal(4, cleared.Tick);
    }

    [Fact]
    public void LowAlarm_ClearsOnlyAfterRisingThroughHysteresis()
    {
        var raised = Assert.IsType<AlarmDecision.Raise>(
            AlarmEngine.Evaluate(9.9, 1, low: 10, high: 100, hysteresis: 2, active: null));
        var active = new ActiveAlarm(raised.Direction, raised.Threshold, raised.Hysteresis);

        Assert.IsType<AlarmDecision.None>(AlarmEngine.Evaluate(11.9, 2, 10, 100, 2, active));
        var cleared = Assert.IsType<AlarmDecision.Clear>(AlarmEngine.Evaluate(12, 3, 10, 100, 2, active));
        Assert.Equal(12, cleared.ClearValue);
    }

    [Fact]
    public void AfterClear_ANewBreachRaisesAgain()
    {
        var raised = Assert.IsType<AlarmDecision.Raise>(
            AlarmEngine.Evaluate(101, 1, low: 0, high: 100, hysteresis: 5, active: null));
        var active = new ActiveAlarm(raised.Direction, raised.Threshold, raised.Hysteresis);
        Assert.IsType<AlarmDecision.Clear>(AlarmEngine.Evaluate(95, 2, 0, 100, 5, active));

        var again = Assert.IsType<AlarmDecision.Raise>(
            AlarmEngine.Evaluate(101, 3, low: 0, high: 100, hysteresis: 5, active: null));
        Assert.Equal(AlarmDirection.High, again.Direction);
        Assert.Equal(3, again.Tick);
    }

    [Fact]
    public void Acknowledge_SetsTheFlag_AndDoesNotClearOrDuplicate()
    {
        var when = new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);
        var acked = new Acknowledgement(false, null).Mark(when);
        var again = acked.Mark(when.AddHours(1));

        Assert.True(acked.IsAcknowledged);
        Assert.Equal(when, acked.AcknowledgedAt);
        Assert.Equal(acked, again);

        var raised = Assert.IsType<AlarmDecision.Raise>(
            AlarmEngine.Evaluate(50, 1, low: null, high: 40, hysteresis: 1.5, active: null));
        var active = new ActiveAlarm(raised.Direction, raised.Threshold, raised.Hysteresis);
        Assert.IsType<AlarmDecision.None>(
            AlarmEngine.Evaluate(42, 2, low: null, high: 40, hysteresis: 1.5, active));
    }

    [Fact]
    public void MissingThresholds_DoNotRaise()
    {
        var decision = AlarmEngine.Evaluate(1_000_000, 3, low: null, high: null, hysteresis: 0, active: null);
        Assert.IsType<AlarmDecision.None>(decision);
    }

    [Fact]
    public void CabinetTemperatureScript_RaisesOnce_Holds_ThenClears()
    {
        var device = FleetCatalog.Find("LC-B1");
        Assert.NotNull(device);
        var temperature = device.Quantities.Single(quantity => quantity.Code == QuantityCodes.CabinetTemperature);
        ActiveAlarm? active = null;
        var raises = 0;
        var clears = 0;

        for (var tick = 0; tick <= 8; tick++)
        {
            var value = ReadingSimulator.ValueAt(FleetCatalog.DefaultSeed, device.Id, temperature, tick);
            var decision = AlarmEngine.Evaluate(
                value,
                tick,
                temperature.Low,
                temperature.High,
                temperature.Hysteresis,
                active);

            switch (decision)
            {
                case AlarmDecision.Raise raised:
                    raises++;
                    active = new ActiveAlarm(raised.Direction, raised.Threshold, raised.Hysteresis);
                    Assert.Equal(5, tick);
                    Assert.Equal(41.0, value);
                    Assert.Equal(AlarmDirection.High, raised.Direction);
                    break;
                case AlarmDecision.Clear:
                    clears++;
                    active = null;
                    Assert.Equal(8, tick);
                    Assert.Equal(38.5, value);
                    break;
                case AlarmDecision.None:
                    break;
                default:
                    throw new InvalidOperationException("Unexpected alarm decision.");
            }
        }

        Assert.Equal(1, raises);
        Assert.Equal(1, clears);
        Assert.Null(active);
    }
}
