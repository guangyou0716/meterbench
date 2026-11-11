using System.Net;
using Meterbench.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meterbench.Tests.Api;

public class ApiConcurrencyTests
{
    [Fact]
    public async Task OverlappingTicks_AdvanceOnceEach_WithoutDuplicateRows()
    {
        using var api = new TempApi();
        Assert.Equal(HttpStatusCode.OK, (await TempApi.PostAsync(api.Client, "/api/seed/reset")).StatusCode);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => TempApi.PostAsync(api.Client, "/api/simulator/tick?count=3")));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var ticks = new List<int>();
        foreach (var response in responses)
        {
            ticks.Add((await JsonBody.ReadAsync(response)).GetProperty("tick").GetInt32());
        }

        // Each call saw a distinct, serialized end tick: 3, 6, ..., 36.
        Assert.Equal(Enumerable.Range(1, 12).Select(i => i * 3), ticks.Order());

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MeterbenchDbContext>();
        var duplicates = await db.Readings
            .GroupBy(row => new { row.DeviceId, row.Tick, row.Quantity })
            .Where(group => group.Count() > 1)
            .CountAsync();
        Assert.Equal(0, duplicates);
        Assert.Equal(36, await db.Readings.MaxAsync(row => row.Tick));
        Assert.Equal(37, await db.Readings.Where(row => row.DeviceId == "MTR-A1").Select(row => row.Tick).Distinct().CountAsync());
    }

    [Fact]
    public async Task ReadsDuringResetsAndTicks_NeverReturn500()
    {
        using var api = new TempApi();
        Assert.Equal(HttpStatusCode.OK, (await TempApi.PostAsync(api.Client, "/api/seed/reset")).StatusCode);

        var work = new List<Task<HttpResponseMessage>>();
        for (var round = 0; round < 15; round++)
        {
            work.Add(TempApi.PostAsync(api.Client, "/api/seed/reset"));
            work.Add(TempApi.PostAsync(api.Client, "/api/simulator/tick?count=4"));
            work.Add(api.Client.GetAsync("/api/devices/MTR-A1/latest"));
            work.Add(api.Client.GetAsync("/api/devices/LC-B1/history?limit=50"));
            work.Add(api.Client.GetAsync("/api/alarms"));
        }

        var responses = await Task.WhenAll(work);
        Assert.All(
            responses,
            response => Assert.True(
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
                $"{response.RequestMessage?.RequestUri} returned {(int)response.StatusCode}"));

        var reset = await JsonBody.ReadAsync(await TempApi.PostAsync(api.Client, "/api/seed/reset"));
        Assert.Equal(0, reset.GetProperty("tick").GetInt32());
        var latest = await JsonBody.ReadAsync(await api.Client.GetAsync("/api/devices/MTR-A1/latest"));
        Assert.Equal(0, latest.GetProperty("tick").GetInt32());
    }

    [Fact]
    public async Task ReadingsTable_RejectsADuplicateDeviceTickQuantity()
    {
        using var api = new TempApi();
        Assert.Equal(HttpStatusCode.OK, (await TempApi.PostAsync(api.Client, "/api/seed/reset")).StatusCode);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MeterbenchDbContext>();
        var existing = await db.Readings.AsNoTracking().FirstAsync(row => row.DeviceId == "MTR-A1" && row.Tick == 0);
        db.Readings.Add(new ReadingRow
        {
            DeviceId = existing.DeviceId,
            Tick = existing.Tick,
            RecordedAt = existing.RecordedAt,
            Quantity = existing.Quantity,
            Value = existing.Value + 1,
            Unit = existing.Unit,
            Obis = existing.Obis
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
