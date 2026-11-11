using System.Buffers.Binary;

namespace Meterbench.Core.Modbus;

/// <summary>
/// Minimal Modbus TCP (MBAP) codec for the simulator. Only function 0x03 Read Holding Registers.
/// MBAP: transaction id (2) | protocol id 0 (2) | length (2, unit id + PDU) | unit id (1), all big-endian.
/// </summary>
public static class ModbusTcpCodec
{
    public const int MbapHeaderLength = 7;
    public const byte ReadHoldingRegisters = 0x03;
    public const ushort MaxReadCount = 125;

    /// <summary>Largest legal MBAP length field: unit id + 253-byte PDU.</summary>
    public const ushort MaxLengthField = 254;

    /// <summary>
    /// Reads the MBAP length field. Returns the total ADU size (header included),
    /// or null when the header is not a Modbus TCP header this simulator accepts.
    /// </summary>
    public static int? AduLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < MbapHeaderLength)
        {
            return null;
        }

        var protocol = BinaryPrimitives.ReadUInt16BigEndian(header[2..]);
        var length = BinaryPrimitives.ReadUInt16BigEndian(header[4..]);
        if (protocol != 0 || length < 2 || length > MaxLengthField)
        {
            return null;
        }

        return 6 + length;
    }

    /// <summary>
    /// Handles one complete request ADU. Returns the response ADU, or null when the request
    /// must be dropped (not Modbus TCP, or the length field does not match the bytes).
    /// </summary>
    public static byte[]? Handle(ReadOnlySpan<byte> request, FleetHoldingRegisters registers)
    {
        ArgumentNullException.ThrowIfNull(registers);
        var total = AduLength(request);
        if (total is null || request.Length != total.Value)
        {
            return null;
        }

        var transactionId = BinaryPrimitives.ReadUInt16BigEndian(request);
        var unitId = request[6];
        var pdu = request[MbapHeaderLength..];
        var function = pdu[0];

        if (function != ReadHoldingRegisters)
        {
            return Exception(transactionId, unitId, function, ModbusExceptionCode.IllegalFunction);
        }

        if (pdu.Length != 5)
        {
            return Exception(transactionId, unitId, function, ModbusExceptionCode.IllegalDataValue);
        }

        var start = BinaryPrimitives.ReadUInt16BigEndian(pdu[1..]);
        var count = BinaryPrimitives.ReadUInt16BigEndian(pdu[3..]);
        if (count is 0 or > MaxReadCount)
        {
            return Exception(transactionId, unitId, function, ModbusExceptionCode.IllegalDataValue);
        }

        if (start + count > 0x10000)
        {
            return Exception(transactionId, unitId, function, ModbusExceptionCode.IllegalDataAddress);
        }

        var deviceId = ModbusUnits.DeviceFor(unitId);
        if (deviceId is null)
        {
            return Exception(transactionId, unitId, function, ModbusExceptionCode.GatewayTargetFailedToRespond);
        }

        if (!registers.TryRead(deviceId, start, count, out var values, out var error, out _))
        {
            return Exception(transactionId, unitId, function, error);
        }

        var byteCount = values.Length * 2;
        var response = new byte[MbapHeaderLength + 2 + byteCount];
        WriteHeader(response, transactionId, (ushort)(1 + 2 + byteCount), unitId);
        response[7] = ReadHoldingRegisters;
        response[8] = (byte)byteCount;
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(9 + (i * 2)), values[i]);
        }

        return response;
    }

    /// <summary>Builds a function 0x03 request ADU (client side; used by tests and docs).</summary>
    public static byte[] BuildReadRequest(ushort transactionId, byte unitId, ushort start, ushort count)
    {
        var request = new byte[MbapHeaderLength + 5];
        WriteHeader(request, transactionId, 6, unitId);
        request[7] = ReadHoldingRegisters;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8), start);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10), count);
        return request;
    }

    private static byte[] Exception(ushort transactionId, byte unitId, byte function, ModbusExceptionCode code)
    {
        var response = new byte[MbapHeaderLength + 2];
        WriteHeader(response, transactionId, 3, unitId);
        response[7] = (byte)(function | 0x80);
        response[8] = (byte)code;
        return response;
    }

    private static void WriteHeader(Span<byte> adu, ushort transactionId, ushort length, byte unitId)
    {
        BinaryPrimitives.WriteUInt16BigEndian(adu, transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(adu[2..], 0);
        BinaryPrimitives.WriteUInt16BigEndian(adu[4..], length);
        adu[6] = unitId;
    }
}
