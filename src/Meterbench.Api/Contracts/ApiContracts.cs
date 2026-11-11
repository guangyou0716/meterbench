namespace Meterbench.Api.Contracts;

public static class ErrorCodes
{
    public const string DeviceNotFound = "DeviceNotFound";

    public const string ReadingNotFound = "ReadingNotFound";

    public const string AlarmNotFound = "AlarmNotFound";

    public const string InvalidLimit = "InvalidLimit";

    public const string InvalidCount = "InvalidCount";

    public const string InvalidAlarmId = "InvalidAlarmId";

    public const string InvalidActive = "InvalidActive";
}

public sealed record ErrorInfo(string Code, string Message);

public sealed record ErrorResponse(ErrorInfo Error);

public sealed record DeviceDto(
    string Id,
    string Name,
    string Kind,
    string Location,
    IReadOnlyList<string> Quantities);

public sealed record QuantityReadingDto(string Quantity, string Obis, string Unit, double Value);

public sealed record LatestDto(
    string DeviceId,
    int Tick,
    DateTimeOffset RecordedAt,
    IReadOnlyList<QuantityReadingDto> Readings);

public sealed record HistoryPointDto(
    int Tick,
    DateTimeOffset RecordedAt,
    IReadOnlyList<QuantityReadingDto> Readings);

public sealed record HistoryDto(string DeviceId, int Limit, IReadOnlyList<HistoryPointDto> Points);

public sealed record AlarmDto(
    long Id,
    string DeviceId,
    string Quantity,
    string Direction,
    double Threshold,
    double Hysteresis,
    double TriggerValue,
    double? ClearValue,
    int RaisedAtTick,
    int? ClearedAtTick,
    bool Active,
    bool Acknowledged,
    DateTimeOffset? AcknowledgedAt);

public sealed record SeedResetDto(int Seed, int Tick, int DeviceCount);

public sealed record TickDto(int Seed, int Tick, int Applied);
