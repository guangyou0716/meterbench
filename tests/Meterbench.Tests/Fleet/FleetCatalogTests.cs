using Meterbench.Core.Fleet;

namespace Meterbench.Tests.Fleet;

public class FleetCatalogTests
{
    [Fact]
    public void Fleet_IsTheInventedThreeDevicePlant()
    {
        Assert.Equal(20261111, FleetCatalog.DefaultSeed);
        Assert.Equal(new[] { "MTR-A1", "MTR-A2", "LC-B1" }, FleetCatalog.Devices.Select(device => device.Id).ToArray());

        var meter = FleetCatalog.Find("MTR-A1");
        Assert.NotNull(meter);
        Assert.Equal("electricityMeter", meter.Kind);
        Assert.Equal("Bay 1 / Line A", meter.Location);
        Assert.Equal("Bay 1 / Line A", FleetCatalog.Find("MTR-A2")!.Location);

        var voltage = meter.Quantities.Single(quantity => quantity.Code == QuantityCodes.VoltageL1);
        Assert.Equal("9.1.1.1.0.255", voltage.Obis);
        Assert.Equal("V", voltage.Unit);
        Assert.Equal((sbyte)-1, voltage.Scale);
        Assert.Equal((ushort)0, voltage.HoldingAddress);
        Assert.Equal(230d, voltage.Baseline);
        Assert.Equal(216d, voltage.Low);
        Assert.Equal(244d, voltage.High);
        Assert.Equal(1d, voltage.Hysteresis);

        var controller = FleetCatalog.Find("LC-B1");
        Assert.NotNull(controller);
        Assert.Equal("lineController", controller.Kind);
        Assert.Equal("Bay 1 / Line B", controller.Location);
        var temperature = controller.Quantities.Single(quantity => quantity.Code == QuantityCodes.CabinetTemperature);
        Assert.Equal("9.2.1.3.0.255", temperature.Obis);
        Assert.Equal("C", temperature.Unit);
        Assert.Equal(40d, temperature.High);
        Assert.Equal(1.5d, temperature.Hysteresis);
        Assert.Null(temperature.Low);
        Assert.Equal(QuantityScript.CabinetTemperature, temperature.Script);
    }
}
