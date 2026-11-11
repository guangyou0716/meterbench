using Meterbench.Core.Fleet;
using Meterbench.Core.Modbus;
using Meterbench.Core.Simulation;

namespace Meterbench.Tests.Modbus;

public class FleetHoldingRegistersTests
{
    private static FleetHoldingRegisters At(int tick) => new(FleetCatalog.DefaultSeed, () => tick);

    [Fact]
    public void VoltageAtTickZero_IsBaselineScaledHighWordFirst()
    {
        var regs = At(0).ReadHoldingRegisters("MTR-A1", 0, 2);
        Assert.Equal(new ushort[] { 0x0000, 0x08FC }, regs);
    }

    [Fact]
    public void CumulativeEnergy_UsesBothWords()
    {
        // 1000 + 4 * 0.25 = 1001.00 kWh, scale -2 => 100100 = 0x0001_8704.
        var regs = At(4).ReadHoldingRegisters("MTR-A1", 8, 2);
        Assert.Equal(new ushort[] { 0x0001, 0x8704 }, regs);
    }

    [Fact]
    public void CabinetTemperatureScript_IsServedOnItsTick()
    {
        var regs = At(5).ReadHoldingRegisters("LC-B1", 4, 2);
        Assert.Equal(new ushort[] { 0x0000, 410 }, regs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(250)]
    public void EveryQuantity_MatchesTheSimulator(int tick)
    {
        var source = At(tick);
        foreach (var device in FleetCatalog.Devices)
        {
            foreach (var quantity in device.Quantities)
            {
                var regs = source.ReadHoldingRegisters(device.Id, quantity.HoldingAddress, 2);
                var scaled = unchecked((int)(((uint)regs[0] << 16) | regs[1]));
                var engineering = (decimal)scaled * (decimal)Math.Pow(10, quantity.Scale);
                var expected = (decimal)ReadingSimulator.ValueAt(FleetCatalog.DefaultSeed, device.Id, quantity, tick);
                var step = (decimal)Math.Pow(10, quantity.Scale);
                Assert.True(
                    Math.Abs(engineering - expected) <= step / 2,
                    $"{device.Id}/{quantity.Code} tick {tick}: {engineering} vs {expected}");
            }
        }
    }

    [Fact]
    public void ContiguousBlock_SpansSeveralQuantities()
    {
        var source = At(3);
        var block = source.ReadHoldingRegisters("MTR-A2", 0, 10);
        for (ushort address = 0; address < 10; address += 2)
        {
            var single = source.ReadHoldingRegisters("MTR-A2", address, 2);
            Assert.Equal(single[0], block[address]);
            Assert.Equal(single[1], block[address + 1]);
        }
    }

    [Fact]
    public void LowWordAlone_IsReadable()
    {
        var regs = At(0).ReadHoldingRegisters("MTR-A1", 1, 1);
        Assert.Equal(new ushort[] { 0x08FC }, regs);
    }

    [Theory]
    [InlineData("MTR-A1", 10, 1)]
    [InlineData("MTR-A1", 8, 3)]
    [InlineData("LC-B1", 6, 1)]
    [InlineData("LC-B1", 65535, 1)]
    public void UnmappedAddress_IsIllegalDataAddress(string device, int start, int count)
    {
        var ok = At(0).TryRead(device, (ushort)start, (ushort)count, out _, out var error, out _);
        Assert.False(ok);
        Assert.Equal(ModbusExceptionCode.IllegalDataAddress, error);
    }

    [Fact]
    public void UnknownDevice_AndZeroCount_AreTypedErrors()
    {
        var source = At(0);
        var ex = Assert.Throws<ModbusReadException>(() => source.ReadHoldingRegisters("NOPE", 0, 2));
        Assert.Equal(ModbusExceptionCode.GatewayTargetFailedToRespond, ex.Code);

        ex = Assert.Throws<ModbusReadException>(() => source.ReadHoldingRegisters("MTR-A1", 0, 0));
        Assert.Equal(ModbusExceptionCode.IllegalDataValue, ex.Code);
    }

    [Fact]
    public void NegativeTick_IsServerDeviceFailure()
    {
        var ok = At(-1).TryRead("MTR-A1", 0, 2, out _, out var error, out _);
        Assert.False(ok);
        Assert.Equal(ModbusExceptionCode.ServerDeviceFailure, error);
    }

    [Theory]
    [InlineData(230.1, -1, 2301)]
    [InlineData(-1.5, -1, -15)]
    [InlineData(0.0005, -3, 1)]
    [InlineData(-0.0005, -3, -1)]
    [InlineData(1234.0, 2, 12)]
    public void TryScale_RoundsAwayFromZero(double value, int scale, int expected)
    {
        Assert.True(FleetHoldingRegisters.TryScale(value, (sbyte)scale, out var scaled));
        Assert.Equal(expected, scaled);
    }

    [Theory]
    [InlineData(1e12, -2)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(1e30, -10)]
    public void TryScale_ReportsOverflow(double value, int scale)
    {
        Assert.False(FleetHoldingRegisters.TryScale(value, (sbyte)scale, out _));
    }

    [Fact]
    public void NegativeScaledValue_IsTwosComplementAcrossWords()
    {
        Assert.True(FleetHoldingRegisters.TryScale(-1.5, -1, out var scaled));
        var raw = unchecked((uint)scaled);
        Assert.Equal((ushort)0xFFFF, (ushort)(raw >> 16));
        Assert.Equal((ushort)0xFFF1, (ushort)(raw & 0xFFFF));
    }
}
