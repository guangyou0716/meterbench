using System.Net;

namespace Meterbench.Tests.Api;

public class ApiValidationTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("501")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("")]
    public async Task History_BadLimit_Is400(string limit)
    {
        using var api = new TempApi();
        var response = await api.Client.GetAsync($"/api/devices/MTR-A1/history?limit={limit}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonBody.AssertError(await JsonBody.ReadAsync(response), "InvalidLimit");
    }

    [Fact]
    public async Task History_OmitsLimit_UsesDefault100()
    {
        using var api = new TempApi();
        var response = await api.Client.GetAsync("/api/devices/MTR-A1/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonBody.ReadAsync(response);
        Assert.Equal(100, body.GetProperty("limit").GetInt32());
        Assert.Equal(1, body.GetProperty("points").GetArrayLength());
        Assert.Equal(0, body.GetProperty("points")[0].GetProperty("tick").GetInt32());
    }

    [Fact]
    public async Task History_Limit1_ReturnsTheNewestTick()
    {
        using var api = new TempApi();
        var tick = await TempApi.PostAsync(api.Client, "/api/simulator/tick?count=3");
        Assert.Equal(HttpStatusCode.OK, tick.StatusCode);

        var response = await api.Client.GetAsync("/api/devices/MTR-A2/history?limit=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonBody.ReadAsync(response);
        Assert.Equal(1, body.GetProperty("limit").GetInt32());
        Assert.Equal(1, body.GetProperty("points").GetArrayLength());
        Assert.Equal(3, body.GetProperty("points")[0].GetProperty("tick").GetInt32());
    }

    [Fact]
    public async Task History_Limit500_IsAccepted()
    {
        using var api = new TempApi();
        var response = await api.Client.GetAsync("/api/devices/LC-B1/history?limit=500");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonBody.ReadAsync(response);
        Assert.Equal(500, body.GetProperty("limit").GetInt32());
        Assert.Equal(1, body.GetProperty("points").GetArrayLength());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1001")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task Tick_BadCount_Is400(string count)
    {
        using var api = new TempApi();
        var response = await TempApi.PostAsync(api.Client, $"/api/simulator/tick?count={count}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonBody.AssertError(await JsonBody.ReadAsync(response), "InvalidCount");
    }

    [Fact]
    public async Task Tick_MissingCount_Is400()
    {
        using var api = new TempApi();
        var response = await TempApi.PostAsync(api.Client, "/api/simulator/tick");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonBody.AssertError(await JsonBody.ReadAsync(response), "InvalidCount");
    }

    [Fact]
    public async Task Tick_Count1And1000_AreAccepted()
    {
        using var api = new TempApi();
        var one = await TempApi.PostAsync(api.Client, "/api/simulator/tick?count=1");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        var oneBody = await JsonBody.ReadAsync(one);
        Assert.Equal(1, oneBody.GetProperty("applied").GetInt32());
        Assert.Equal(1, oneBody.GetProperty("tick").GetInt32());
        Assert.Equal(20261111, oneBody.GetProperty("seed").GetInt32());

        var thousand = await TempApi.PostAsync(api.Client, "/api/simulator/tick?count=1000");
        Assert.Equal(HttpStatusCode.OK, thousand.StatusCode);
        var thousandBody = await JsonBody.ReadAsync(thousand);
        Assert.Equal(1000, thousandBody.GetProperty("applied").GetInt32());
        Assert.Equal(1001, thousandBody.GetProperty("tick").GetInt32());
    }

    [Fact]
    public async Task Alarms_BadActiveFilter_Is400()
    {
        using var api = new TempApi();
        var response = await api.Client.GetAsync("/api/alarms?active=maybe");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonBody.AssertError(await JsonBody.ReadAsync(response), "InvalidActive");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    public async Task Ack_BadId_Is400(string id)
    {
        using var api = new TempApi();
        var response = await TempApi.PostAsync(api.Client, $"/api/alarms/{id}/ack");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonBody.AssertError(await JsonBody.ReadAsync(response), "InvalidAlarmId");
    }
}
