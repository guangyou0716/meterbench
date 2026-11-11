using Meterbench.Core.Fleet;
using Meterbench.Core.Modbus;

namespace Meterbench.Tests.Modbus;

public class ModbusTcpCodecTests
{
    private static readonly FleetHoldingRegisters Tick0 = new(FleetCatalog.DefaultSeed, () => 0);

    [Fact]
    public void BuildReadRequest_HasMbapHeaderAndPdu()
    {
        var request = ModbusTcpCodec.BuildReadRequest(0x1234, 1, 0, 2);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 }, request);
    }

    [Fact]
    public void ReadHolding_ReturnsRegistersAndEchoesTransactionAndUnit()
    {
        var response = ModbusTcpCodec.Handle(ModbusTcpCodec.BuildReadRequest(0xBEEF, 1, 0, 2), Tick0);
        Assert.Equal(
            new byte[] { 0xBE, 0xEF, 0x00, 0x00, 0x00, 0x07, 0x01, 0x03, 0x04, 0x00, 0x00, 0x08, 0xFC },
            response);
    }

    [Fact]
    public void UnsupportedFunction_IsIllegalFunction()
    {
        var request = ModbusTcpCodec.BuildReadRequest(7, 1, 0, 2);
        request[7] = 0x04;
        var response = ModbusTcpCodec.Handle(request, Tick0);
        Assert.Equal(new byte[] { 0x00, 0x07, 0x00, 0x00, 0x00, 0x03, 0x01, 0x84, 0x01 }, response);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(126)]
    public void BadCount_IsIllegalDataValue(int count)
    {
        var response = ModbusTcpCodec.Handle(ModbusTcpCodec.BuildReadRequest(1, 1, 0, (ushort)count), Tick0)!;
        Assert.Equal(0x83, response[7]);
        Assert.Equal((byte)ModbusExceptionCode.IllegalDataValue, response[8]);
    }

    [Fact]
    public void AddressPastEnd_IsIllegalDataAddress()
    {
        var response = ModbusTcpCodec.Handle(ModbusTcpCodec.BuildReadRequest(1, 1, 65535, 2), Tick0)!;
        Assert.Equal(0x83, response[7]);
        Assert.Equal((byte)ModbusExceptionCode.IllegalDataAddress, response[8]);

        response = ModbusTcpCodec.Handle(ModbusTcpCodec.BuildReadRequest(1, 3, 6, 2), Tick0)!;
        Assert.Equal((byte)ModbusExceptionCode.IllegalDataAddress, response[8]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(255)]
    public void UnknownUnit_IsGatewayTargetFailed(int unit)
    {
        var response = ModbusTcpCodec.Handle(ModbusTcpCodec.BuildReadRequest(1, (byte)unit, 0, 2), Tick0)!;
        Assert.Equal((byte)unit, response[6]);
        Assert.Equal((byte)ModbusExceptionCode.GatewayTargetFailedToRespond, response[8]);
    }

    [Fact]
    public void ExtraPduBytes_AreIllegalDataValue()
    {
        var request = new byte[13];
        ModbusTcpCodec.BuildReadRequest(1, 1, 0, 2).CopyTo(request, 0);
        request[5] = 7;
        var response = ModbusTcpCodec.Handle(request, Tick0)!;
        Assert.Equal((byte)ModbusExceptionCode.IllegalDataValue, response[8]);
    }

    [Fact]
    public void NonModbusOrMismatchedLength_IsDropped()
    {
        var request = ModbusTcpCodec.BuildReadRequest(1, 1, 0, 2);
        var wrongProtocol = (byte[])request.Clone();
        wrongProtocol[3] = 1;
        Assert.Null(ModbusTcpCodec.Handle(wrongProtocol, Tick0));

        Assert.Null(ModbusTcpCodec.Handle(request.AsSpan(0, 10), Tick0));

        var tooShortLength = (byte[])request.Clone();
        tooShortLength[5] = 1;
        Assert.Null(ModbusTcpCodec.AduLength(tooShortLength));

        var tooLong = (byte[])request.Clone();
        tooLong[4] = 0x01;
        tooLong[5] = 0x00;
        Assert.Null(ModbusTcpCodec.AduLength(tooLong));
        Assert.Null(ModbusTcpCodec.AduLength(request.AsSpan(0, 6)));
    }

    [Fact]
    public void UnitMap_CoversTheFleet()
    {
        Assert.Equal(FleetCatalog.Devices.Select(d => d.Id).Order(), ModbusUnits.Map.Values.Order());
        Assert.Equal("LC-B1", ModbusUnits.DeviceFor(3));
        Assert.Null(ModbusUnits.DeviceFor(9));
    }
}
