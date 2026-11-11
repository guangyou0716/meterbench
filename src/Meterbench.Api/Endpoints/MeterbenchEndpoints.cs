using System.Globalization;
using Meterbench.Api.Contracts;
using Meterbench.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Meterbench.Api.Endpoints;

public static class MeterbenchEndpoints
{
    public const int HistoryLimitDefault = 100;
    public const int HistoryLimitMin = 1;
    public const int HistoryLimitMax = 500;
    public const int TickCountMin = 1;
    public const int TickCountMax = 1000;

    public static void MapMeterbenchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/devices", (FleetService fleet) => Results.Ok(fleet.ListDevices()));

        app.MapGet("/api/devices/{id}/latest", async (
            [FromRoute] string id,
            FleetService fleet,
            CancellationToken cancellationToken) =>
        {
            var lookup = await fleet.GetLatestAsync(id, cancellationToken);
            return lookup switch
            {
                LatestLookup.Ok ok => Results.Ok(ok.Body),
                LatestLookup.MissingDevice => ApiError(
                    StatusCodes.Status404NotFound,
                    ErrorCodes.DeviceNotFound,
                    $"Device '{id}' was not found."),
                LatestLookup.MissingReading => ApiError(
                    StatusCodes.Status404NotFound,
                    ErrorCodes.ReadingNotFound,
                    $"Device '{id}' has no readings."),
                _ => throw new InvalidOperationException("Unexpected latest-reading result.")
            };
        });

        app.MapGet("/api/devices/{id}/history", async (
            [FromRoute] string id,
            [FromQuery] string? limit,
            FleetService fleet,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveLimit(limit, out var parsedLimit))
            {
                return ApiError(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.InvalidLimit,
                    "Query 'limit' must be an integer from 1 to 500.");
            }

            var lookup = await fleet.GetHistoryAsync(id, parsedLimit, cancellationToken);
            return lookup switch
            {
                HistoryLookup.Ok ok => Results.Ok(ok.Body),
                HistoryLookup.MissingDevice => ApiError(
                    StatusCodes.Status404NotFound,
                    ErrorCodes.DeviceNotFound,
                    $"Device '{id}' was not found."),
                _ => throw new InvalidOperationException("Unexpected history result.")
            };
        });

        app.MapGet("/api/alarms", async (
            [FromQuery] string? active,
            FleetService fleet,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveActive(active, out var activeFilter))
            {
                return ApiError(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.InvalidActive,
                    "Query 'active' must be 'true' or 'false' when it is present.");
            }

            return Results.Ok(await fleet.ListAlarmsAsync(activeFilter, cancellationToken));
        });

        app.MapPost("/api/alarms/{id}/ack", async (
            [FromRoute] string id,
            FleetService fleet,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseAlarmId(id, out var alarmId))
            {
                return ApiError(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.InvalidAlarmId,
                    "Alarm id must be a positive integer.");
            }

            var lookup = await fleet.AcknowledgeAsync(alarmId, cancellationToken);
            return lookup switch
            {
                AckLookup.Ok ok => Results.Ok(ok.Body),
                AckLookup.Missing => ApiError(
                    StatusCodes.Status404NotFound,
                    ErrorCodes.AlarmNotFound,
                    $"Alarm '{alarmId}' was not found."),
                _ => throw new InvalidOperationException("Unexpected acknowledge result.")
            };
        });

        app.MapPost("/api/simulator/tick", async (
            [FromQuery] string? count,
            FleetService fleet,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveCount(count, out var parsedCount))
            {
                return ApiError(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.InvalidCount,
                    "Query 'count' must be an integer from 1 to 1000.");
            }

            return Results.Ok(await fleet.TickAsync(parsedCount, cancellationToken));
        });

        app.MapPost("/api/seed/reset", async (FleetService fleet, CancellationToken cancellationToken) =>
            Results.Ok(await fleet.ResetAsync(cancellationToken)));
    }

    private static IResult ApiError(int statusCode, string code, string message)
    {
        return Results.Json(new ErrorResponse(new ErrorInfo(code, message)), statusCode: statusCode);
    }

    private static bool TryResolveLimit(string? text, out int limit)
    {
        if (text is null)
        {
            limit = HistoryLimitDefault;
            return true;
        }

        return TryParseBounded(text, HistoryLimitMin, HistoryLimitMax, out limit);
    }

    private static bool TryResolveCount(string? text, out int count)
    {
        if (text is null)
        {
            count = 0;
            return false;
        }

        return TryParseBounded(text, TickCountMin, TickCountMax, out count);
    }

    private static bool TryResolveActive(string? text, out bool? active)
    {
        active = null;
        if (text is null)
        {
            return true;
        }

        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            active = true;
            return true;
        }

        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
        {
            active = false;
            return true;
        }

        return false;
    }

    private static bool TryParseAlarmId(string text, out long id)
    {
        id = 0;
        if (text.Length is 0 or > 18)
        {
            return false;
        }

        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id))
        {
            return false;
        }

        return id >= 1;
    }

    private static bool TryParseBounded(string text, int min, int max, out int value)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return value >= min && value <= max;
    }
}
