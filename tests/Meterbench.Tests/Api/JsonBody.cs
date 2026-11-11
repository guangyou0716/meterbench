using System.Text.Json;

namespace Meterbench.Tests.Api;

internal static class JsonBody
{
    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    public static void AssertError(JsonElement body, string code)
    {
        Assert.Equal(new[] { "error" }, body.EnumerateObject().Select(property => property.Name).ToArray());
        var error = body.GetProperty("error");
        Assert.Equal(new[] { "code", "message" }, error.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }
}
